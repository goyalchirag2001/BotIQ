using BotIQ.Api;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5070");
builder.Services.AddSingleton<BrowserProbe>();
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
app.Run();

static async Task<IResult> Run<T>(Func<Task<T>> action)
{
    try { return Results.Ok(await action()); }
    catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 409); }
    catch (HttpRequestException ex) { return Results.Problem($"Demo provider unavailable: {ex.Message}", statusCode: 502); }
    catch (Microsoft.Playwright.PlaywrightException ex) { return Results.Problem($"Browser probe failed: {ex.Message}", statusCode: 502); }
}
