using BotIQ.Api;
using BotIQ.CodeIntelligence;
using BotIQ.Core;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5070");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 6_500_000);
builder.Services.AddSingleton<BotRegistry>();
builder.Services.AddSingleton<BrowserProbe>();
builder.Services.AddSingleton<WorkflowDiscovery>();
builder.Services.AddSingleton(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    { BaseAddress = BrowserProbe.Provider, Timeout = TimeSpan.FromSeconds(15) });
builder.Services.AddSingleton<InvestigationService>();
var app = builder.Build();
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
app.MapGet("/api/state", async (InvestigationService svc, CancellationToken ct) =>
    Results.Ok(new { version = await svc.VersionAsync(ct), baseline = await svc.GetBaselineAsync(ct) }));
app.MapPost("/api/provider/{version:int}", async (int version, InvestigationService svc, CancellationToken ct) =>
    await Run(async () => { await svc.SetVersionAsync(version, ct); return new { version }; }));
app.MapPost("/api/baseline", async (InvestigationService svc, CancellationToken ct) =>
    await Run(() => svc.CaptureBaselineAsync(ct)));
app.MapPost("/api/investigate", async (InvestigationService svc, CancellationToken ct) =>
    await Run(() => svc.InvestigateAsync(ct)));
app.MapGet("/api/bots", async (BotRegistry registry, CancellationToken ct) => Results.Ok(await registry.ListAsync(ct)));
app.MapGet("/api/bots/{id:guid}", async (Guid id, BotRegistry registry, CancellationToken ct) =>
    await registry.GetAsync(id, ct) is { } bot ? Results.Ok(bot) : Results.NotFound());
app.MapPost("/api/bots", async (RegisterBot input, BotRegistry registry, CancellationToken ct) =>
    await Run(() => registry.RegisterAsync(input, ct)));
// The live probe still executes ONLY the compiled synthetic sample, not uploaded source.
app.MapPost("/api/bots/{id:guid}/demo-investigation", async (Guid id, BotRegistry registry, InvestigationService svc, CancellationToken ct) =>
{
    var bot = await registry.GetAsync(id, ct);
    if (bot is null) return Results.NotFound();
    return await Run(async () =>
    {
        var investigation = await svc.InvestigateAsync(ct);
        var impact = SourceAnalyzer.MapImpact(bot.Analysis, investigation.HistoricalVsLive);
        return new { investigation, sourceSnapshot = new { bot.Id, bot.Name, bot.Version, bot.SourceHash }, impact };
    });
});
app.MapPost("/api/discovery/capture", async (BrowserWorkflow workflow, WorkflowDiscovery discovery, CancellationToken ct) =>
    await Run(() => discovery.CaptureAsync(workflow, ct)));
app.MapPost("/api/discovery/har", (JsonElement har) =>
    Run(() => Task.FromResult(WorkflowDiscovery.ImportHar(har))));
app.MapPost("/api/discovery/compare", async (DiscoveryCompare input, WorkflowDiscovery discovery, BotRegistry registry, CancellationToken ct) =>
    await Run(async () =>
    {
        var baseline = input.Baseline;
        if (baseline is null || baseline.Path is null || !baseline.Path.StartsWith("/api/", StringComparison.Ordinal) || baseline.Method is not ("GET" or "POST") || baseline.Status is < 200 or >= 300 || baseline.Headers is null || baseline.RequestShape is null || baseline.ResponseShape is null || baseline.RequestShape.Count > 1000 || baseline.ResponseShape.Count > 1000 || baseline.Headers.Count > 2)
            throw new ArgumentException("Choose a valid successful lab baseline fingerprint.");
        var bot = input.BotId is { } id ? await registry.GetAsync(id, ct) : null;
        if (input.BotId.HasValue && bot is null) throw new ArgumentException("Source snapshot not found.");
        var capture = await discovery.CaptureAsync(input.Workflow, ct);
        if (capture.Truncated) throw new InvalidOperationException("Capture was incomplete; no match was selected.");
        var match = Discovery.Match(baseline, capture.Requests);
        var impact = bot is null ? null : SourceAnalyzer.MapImpact(bot.Analysis, match.Differences);
        return new { capture, match, impact };
    }));
app.Run();

static async Task<IResult> Run<T>(Func<Task<T>> action)
{
    try { return Results.Ok(await action()); }
    catch (ArgumentException ex) { return Results.Problem(ex.Message, statusCode: 400); }
    catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 409); }
    catch (TimeoutException) { return Results.Problem("Browser capture timed out.", statusCode: 504); }
    catch (HttpRequestException ex) { return Results.Problem($"Demo provider unavailable: {ex.Message}", statusCode: 502); }
    catch (Microsoft.Playwright.PlaywrightException ex) { return Results.Problem($"Browser probe failed: {ex.Message}", statusCode: 502); }
}
