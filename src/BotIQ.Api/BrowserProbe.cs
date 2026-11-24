using BotIQ.Core;
using Microsoft.Playwright;
using System.Diagnostics;

namespace BotIQ.Api;

public sealed record RawCapture(IReadOnlyList<Exchange> Exchanges, bool Truncated, IReadOnlyList<string> Diagnostics);

public sealed class BrowserProbe
{
    public static readonly Uri Provider = new("http://127.0.0.1:5071/");
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<Exchange> CaptureAsync(Scenario scenario, CancellationToken ct = default)
    {
        var capture = await CaptureWorkflowAsync(new("Hotel rate search", [new("fill", "#property", scenario.PropertyId), new("click", "#search", null), new("wait", "#results:not(:empty)", null)]), ct);
        var posts = capture.Exchanges.Where(e => e.Method == "POST").ToArray();
        if (capture.Truncated || posts.Length != 1) throw new InvalidOperationException("Expected exactly one complete search POST in the synthetic workflow; inspect Discovery diagnostics.");
        return posts[0];
    }

    public async Task<RawCapture> CaptureWorkflowAsync(BrowserWorkflow workflow, CancellationToken ct)
    {
        WorkflowDiscovery.Validate(workflow);
        if (!await gate.WaitAsync(0, ct)) throw new InvalidOperationException("A browser investigation is already running.");
        var stage = "provider preflight";
        var watch = Stopwatch.StartNew();
        var notes = new List<string>();
        var timeout = int.TryParse(Environment.GetEnvironmentVariable("BOTIQ_BROWSER_TIMEOUT_MS"), out var configured) ? Math.Clamp(configured, 15000, 120000) : 60000;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMilliseconds(timeout * 3L));
        var token = budget.Token;
        try
        {
            using (var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) })
            {
                using var result = await http.GetAsync(new Uri(Provider, "/api/state"), token);
                if (!result.IsSuccessStatusCode) throw new HttpRequestException($"Provider state returned HTTP {(int)result.StatusCode}.");
            }
            notes.Add("Provider preflight succeeded.");
            stage = "Playwright initialization";
            using var playwright = await Playwright.CreateAsync();
            token.ThrowIfCancellationRequested();
            stage = "Chromium launch";
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = Environment.GetEnvironmentVariable("BOTIQ_BROWSER_HEADLESS") != "0", Timeout = timeout, Args = ["--no-proxy-server"] });
            stage = "browser context setup";
            await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block });
            // Keep the lab isolated. No external origins, arbitrary navigation or administrative routes.
            await context.RouteAsync("**/*", route =>
            {
                if (!WorkflowDiscovery.Allowed(route.Request.Url)) return route.AbortAsync();
                var path = new Uri(route.Request.Url).AbsolutePath;
                return (path == "/" && route.Request.Method == "GET") || WorkflowDiscovery.AllowedRequest(route.Request.Url, route.Request.Method)
                    ? route.ContinueAsync() : route.AbortAsync();
            });
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(timeout);
            page.SetDefaultNavigationTimeout(timeout);
            var responses = new List<IResponse>();
            var failures = new List<string>();
            var truncated = false;
            page.Response += (_, response) =>
            {
                if (!WorkflowDiscovery.AllowedRequest(response.Url, response.Request.Method) || response.Request.ResourceType is not ("fetch" or "xhr")) return;
                lock (responses) { if (responses.Count < 100) responses.Add(response); else truncated = true; }
            };
            page.RequestFailed += (_, request) =>
            {
                if (WorkflowDiscovery.AllowedRequest(request.Url, request.Method)) lock (failures) { if (failures.Count < 10) failures.Add($"Failed request: {request.Method} {new Uri(request.Url).AbsolutePath}"); }
            };
            page.PageError += (_, _) => { lock (failures) { if (failures.Count < 10) failures.Add("Provider page reported a JavaScript error."); } };
            stage = "navigation to local provider";
            var navigation = await page.GotoAsync(Provider.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = timeout }).WaitAsync(token);
            if (navigation is null || !navigation.Ok) throw new HttpRequestException($"Provider navigation returned HTTP {navigation?.Status}.");
            notes.Add("Chromium navigation succeeded.");
            foreach (var step in workflow.Steps)
            {
                token.ThrowIfCancellationRequested();
                stage = $"workflow {step.Action} {step.Selector}";
                var locator = page.Locator(step.Selector);
                switch (step.Action)
                {
                    case "fill": await locator.FillAsync(step.Value!, new() { Timeout = timeout }).WaitAsync(token); break;
                    case "click": await locator.ClickAsync(new() { Timeout = timeout }).WaitAsync(token); break;
                    case "wait":
                        // Wait for actual completion rather than visibility of a possibly empty element.
                        await page.Locator("#results[data-state='complete'], #results[data-state='error']").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = timeout }).WaitAsync(token);
                        if (await page.Locator("#results").GetAttributeAsync("data-state").WaitAsync(token) == "error") throw new InvalidOperationException("Provider search failed in the page. Open http://127.0.0.1:5071 manually and check the search.");
                        break;
                }
            }
            stage = "reading captured response bodies";
            IResponse[] snapshot; lock (responses) snapshot = responses.ToArray();
            var exchanges = new List<Exchange>();
            long totalChars = 0;
            foreach (var response in snapshot)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var post = response.Request.PostData ?? "";
                    var body = await response.TextAsync().WaitAsync(TimeSpan.FromMilliseconds(timeout), token);
                    if (post.Length > Discovery.MaxBody || body.Length > Discovery.MaxBody || (totalChars += (long)post.Length + body.Length) > 32_000_000) { truncated = true; continue; }
                    var headers = await response.Request.AllHeadersAsync().WaitAsync(token);
                    var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (key, value) in headers)
                        if (key.Equals("content-type", StringComparison.OrdinalIgnoreCase) || key.Equals("x-demo-contract", StringComparison.OrdinalIgnoreCase))
                            selected[key] = key.Equals("content-type", StringComparison.OrdinalIgnoreCase) ? value.Split(';')[0].Trim() : value;
                    exchanges.Add(new Exchange("independent-browser", response.Request.Method, new Uri(response.Url).AbsolutePath, selected, post, response.Status, body));
                }
                catch (Exception ex) when (ex is System.TimeoutException or PlaywrightException)
                { truncated = true; notes.Add($"Could not read {response.Request.Method} {new Uri(response.Url).AbsolutePath}: {ex.GetType().Name}."); }
            }
            lock (failures) { notes.AddRange(failures); if (failures.Count > 0) truncated = true; }
            if (!exchanges.Any(e => e.Method == "POST")) throw new InvalidOperationException("Browser completed but no search POST was captured. Check the provider page and restart both updated servers.");
            notes.Add($"Captured {exchanges.Count} responses in {watch.ElapsedMilliseconds} ms.");
            return new(exchanges, truncated, notes);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new System.TimeoutException($"Browser capture timed out during {stage} after {watch.ElapsedMilliseconds} ms. Check provider and Chromium; see Batch 04 troubleshooting."); }
        catch (System.TimeoutException ex) { throw new System.TimeoutException($"Browser capture timed out during {stage} after {watch.ElapsedMilliseconds} ms. {ex.Message.Split('\n')[0]}", ex); }
        catch (PlaywrightException ex) { throw new InvalidOperationException($"Browser capture failed during {stage}: {ex.Message.Split('\n')[0]}. Check Chromium installation and provider availability.", ex); }
        finally { gate.Release(); }
    }
}
