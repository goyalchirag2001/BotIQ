using BotIQ.Api;
using BotIQ.CodeIntelligence;
using BotIQ.Core;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5070");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 104_857_600);
builder.Services.AddSingleton<BotRegistry>();
builder.Services.AddSingleton<BrowserProbe>();
builder.Services.AddSingleton<WorkflowDiscovery>();
builder.Services.AddSingleton<RuntimeRegistry>();
builder.Services.AddSingleton(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    { BaseAddress = BrowserProbe.Provider, Timeout = TimeSpan.FromSeconds(15) });
builder.Services.AddSingleton<InvestigationService>();
var connection = builder.Configuration.GetConnectionString("BotIQ");
if (!string.IsNullOrWhiteSpace(connection)) builder.Services.AddDbContext<HistoryDb>(options => options.UseNpgsql(connection));
builder.Services.AddSingleton<InvestigationHistory>();
var app = builder.Build();
var history = app.Services.GetRequiredService<InvestigationHistory>();
await history.InitializeAsync(CancellationToken.None);
app.Use(async (context, next) =>
{
    // The unauthenticated lab is local-only; reject cross-origin mutation attempts.
    if (HttpMethods.IsPost(context.Request.Method) &&
        (!context.Request.Headers.TryGetValue("X-BotIQ-Lab", out var marker) || marker != "1"))
    { context.Response.StatusCode = 403; return; }
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/discovery/progress", (BrowserProbe probe) => Results.Ok(probe.Progress));
app.MapGet("/api/health", () => Results.Ok(new { service = "BotIQ.Api", version = "0.5.4" }));
app.MapGet("/lab", (IWebHostEnvironment env) => Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html; charset=utf-8"));
app.MapGet("/api/state", async (InvestigationService svc, CancellationToken ct) =>
    Results.Ok(new { version = await svc.VersionAsync(ct), baseline = await svc.GetBaselineAsync(ct) }));
app.MapPost("/api/provider/{version:int}", async (int version, InvestigationService svc, CancellationToken ct) =>
    await Run(async () => { await svc.SetVersionAsync(version, ct); return new { version }; }));
app.MapPost("/api/baseline", async (InvestigationService svc, CancellationToken ct) =>
    await Run(() => svc.CaptureBaselineAsync(ct)));
app.MapPost("/api/investigate", async (InvestigationService svc, InvestigationHistory historyStore, CancellationToken ct) =>
    await Run(async () => { var report = await svc.InvestigateAsync(ct); await historyStore.RecordDemoAsync(report, null, ct); return report; }));
app.MapGet("/api/bots", async (BotRegistry registry, CancellationToken ct) => Results.Ok(await registry.ListAsync(ct)));
app.MapGet("/api/bots/{id:guid}", async (Guid id, BotRegistry registry, CancellationToken ct) =>
    await registry.GetAsync(id, ct) is { } bot ? Results.Ok(bot) : Results.NotFound());
app.MapPost("/api/bots", async (RegisterBot input, BotRegistry registry, CancellationToken ct) =>
    await Run(() => registry.RegisterAsync(input, ct)));
// The live probe still executes ONLY the compiled synthetic sample, not uploaded source.
app.MapPost("/api/bots/{id:guid}/demo-investigation", async (Guid id, BotRegistry registry, InvestigationService svc, InvestigationHistory historyStore, CancellationToken ct) =>
{
    var bot = await registry.GetAsync(id, ct);
    if (bot is null) return Results.NotFound();
    return await Run(async () =>
    {
        var investigation = await svc.InvestigateAsync(ct);
        await historyStore.RecordDemoAsync(investigation, id, ct);
        var impact = SourceAnalyzer.MapImpact(bot.Analysis, investigation.HistoricalVsLive);
        return new { investigation, sourceSnapshot = new { bot.Id, bot.Name, bot.Version, bot.SourceHash }, impact };
    });
});
app.MapPost("/api/discovery/capture", async (BrowserWorkflow workflow, WorkflowDiscovery discovery, CancellationToken ct) =>
    await Run(() => discovery.CaptureAsync(workflow, ct)));
app.MapPost("/api/discovery/har", (JsonElement har) =>
    Run(() => Task.FromResult(WorkflowDiscovery.ImportHar(har))));
app.MapPost("/api/discovery/compare", async (DiscoveryCompare input, WorkflowDiscovery discovery, BotRegistry registry, InvestigationHistory historyStore, CancellationToken ct) =>
    await Run(async () =>
    {
        var baseline = input.Baseline;
        if (baseline is null || baseline.Path is null || !baseline.Path.StartsWith("/api/", StringComparison.Ordinal) || baseline.Method is not ("GET" or "POST") || baseline.Status is < 200 or >= 300 || baseline.Headers is null || baseline.RequestShape is null || baseline.ResponseShape is null || baseline.Incomplete || baseline.RequestShape.Count > Discovery.MaxShapePaths || baseline.ResponseShape.Count > Discovery.MaxShapePaths || baseline.Headers.Count > 2)
            throw new ArgumentException("Choose a valid successful lab baseline fingerprint.");
        var bot = input.BotId is { } id ? await registry.GetAsync(id, ct) : null;
        if (input.BotId.HasValue && bot is null) throw new ArgumentException("Source snapshot not found.");
        var capture = await discovery.CaptureAsync(input.Workflow, ct);
        if (capture.Truncated) throw new InvalidOperationException("Capture was incomplete; no match was selected. " + string.Join(" ", capture.Diagnostics));
        var match = Discovery.Match(baseline, capture.Requests);
        var impact = bot is null ? null : SourceAnalyzer.MapImpact(bot.Analysis, match.Differences);
        await historyStore.RecordDiscoveryAsync(match, input.BotId, ct);
        return new { capture, match, impact };
    }));
app.MapGet("/api/bots/{id:guid}/dependencies", async (Guid id, BotRegistry registry, CancellationToken ct) =>
    await Run(() => registry.DependenciesAsync(id, ct)));
app.MapPost("/api/bots/{id:guid}/runtime", async (Guid id, RuntimeTrace trace, RuntimeRegistry runtime, CancellationToken ct) =>
    await Run(() => runtime.SaveAsync(id, trace, ct)));
app.MapGet("/api/bots/{id:guid}/runtime", async (Guid id, RuntimeRegistry runtime, CancellationToken ct) =>
    await Run(() => runtime.ListAsync(id, ct)));
app.MapPost("/api/bots/{id:guid}/profiles", async (Guid id, DiscoveryProfile profile, RuntimeRegistry runtime, CancellationToken ct) =>
    await Run(() => runtime.SaveProfileAsync(id, profile, ct)));
app.MapGet("/api/bots/{id:guid}/profiles", async (Guid id, RuntimeRegistry runtime, CancellationToken ct) =>
    await Run(() => runtime.ListProfilesAsync(id, ct)));
app.MapPost("/api/history/external", (ExternalReviewSummary input, BotRegistry registry, InvestigationHistory store, CancellationToken ct) =>
    Run(async () =>
    {
        if (input.Matched is < 0 or > 100 || input.Ambiguous is < 0 or > 100 || input.Missing is < 0 or > 100 || input.Matched + input.Ambiguous + input.Missing > 100 || input.Differences is < 0 or > 10_000_000 || input.SourceHits is < 0 or > 10000 || (!input.HasSecond && (input.Matched + input.Ambiguous + input.Missing + input.Differences != 0)))
            throw new ArgumentException("Invalid external review counts.");
        if (input.BotId is { } id && await registry.GetAsync(id, ct) is null) throw new ArgumentException("Bot snapshot not found.");
        var entry = new HistoryEntry { BotId = input.BotId, Kind = "external", Outcome = input.HasSecond ? "Candidate evidence review" : "Single capture review", DifferenceCount = input.Differences, Details = $"Source hits: {input.SourceHits}; candidate matches: {input.Matched}; ambiguous: {input.Ambiguous}; not observed: {input.Missing}" };
        await store.SaveAsync(entry, ct);
        return new { entry.Id };
    }));
app.MapGet("/api/history", async (int? limit, Guid? botId, InvestigationHistory historyStore, CancellationToken ct) =>
    await Run(() => historyStore.ListAsync(limit ?? 50, botId, ct)));
app.MapGet("/api/platform", (InvestigationHistory historyStore) => Results.Ok(new { version = "0.5.4", historyStorage = historyStore.UsesPostgres ? "PostgreSQL" : "Local files", providerUrl = BrowserProbe.Provider.AbsoluteUri }));
app.Run();

static async Task<IResult> Run<T>(Func<Task<T>> action)
{
    try { return Results.Ok(await action()); }
    catch (ArgumentException ex) { return Results.Problem(ex.Message, statusCode: 400); }
    catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 409); }
    catch (TimeoutException ex) { return Results.Problem(ex.Message, statusCode: 504); }
    catch (HttpRequestException ex) { return Results.Problem($"Demo provider unavailable: {ex.Message}", statusCode: 502); }
    catch (Microsoft.Playwright.PlaywrightException ex) { return Results.Problem($"Browser probe failed: {ex.Message}", statusCode: 502); }
}

public sealed record ExternalReviewSummary(Guid? BotId, int Matched, int Ambiguous, int Missing, int Differences, int SourceHits, bool HasSecond);
