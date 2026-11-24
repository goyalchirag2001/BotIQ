using System.Text;
using System.Text.Json;
using BotIQ.Core;
using Microsoft.Playwright;

namespace BotIQ.Api;

public sealed record BrowserStep(string Action, string Selector, string? Value);
public sealed record BrowserWorkflow(string Name, IReadOnlyList<BrowserStep> Steps);
public sealed record CaptureReport(string Workflow, IReadOnlyList<Fingerprint> Requests, bool Truncated);
public sealed record DiscoveryCompare(Fingerprint Baseline, BrowserWorkflow Workflow, Guid? BotId = null);

public sealed class WorkflowDiscovery
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public static bool Allowed(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == BrowserProbe.Provider.Scheme && uri.Host == BrowserProbe.Provider.Host && uri.Port == BrowserProbe.Provider.Port && string.IsNullOrEmpty(uri.UserInfo);
    public static bool AllowedRequest(string url, string method) => Allowed(url) && new Uri(url).AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) && method is "GET" or "POST";
    public static void Validate(BrowserWorkflow workflow)
    {
        if (workflow is null || string.IsNullOrWhiteSpace(workflow.Name) || workflow.Name.Length > 100 || workflow.Steps is null || workflow.Steps.Count is < 1 or > 8)
            throw new ArgumentException("Workflow needs a name and 1–8 steps.");
        foreach (var step in workflow.Steps)
        {
            if (step is null || step.Action is not ("fill" or "click" or "wait") || string.IsNullOrWhiteSpace(step.Selector) || step.Selector.Length > 200 || (step.Value?.Length ?? 0) > 100 || (step.Action == "fill" && step.Value is null))
                throw new ArgumentException("Use fill, click or wait with a CSS selector; fill requires a value.");
            // Lab only: no arbitrary JS, navigation actions, credentials or production target URLs.
            if (step.Selector is not ("#property" or "#search" or "#results:not(:empty)")) throw new ArgumentException("Selector is outside the synthetic lab allowlist.");
            if ((step.Action == "fill" && step.Selector != "#property") || (step.Action == "click" && step.Selector != "#search") || (step.Action == "wait" && step.Selector != "#results:not(:empty)"))
                throw new ArgumentException("Action is not allowed for this selector.");
        }
        if (workflow.Steps.Count(x => x.Action == "click") != 1 || workflow.Steps[^1].Action != "wait") throw new ArgumentException("Use one search click and finish with a results wait.");
    }
    public async Task<CaptureReport> CaptureAsync(BrowserWorkflow workflow, CancellationToken ct)
    {
        Validate(workflow);
        if (!await gate.WaitAsync(0, ct)) throw new InvalidOperationException("A browser investigation is already running.");
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Timeout = 10000 });
            await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block });
            await context.RouteAsync("**/*", route =>
            {
                if (!Allowed(route.Request.Url)) return route.AbortAsync();
                var path = new Uri(route.Request.Url).AbsolutePath;
                return (path == "/" && route.Request.Method == "GET") || AllowedRequest(route.Request.Url, route.Request.Method)
                    ? route.ContinueAsync() : route.AbortAsync();
            });
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(4000);
            var responses = new List<IResponse>(); bool truncated = false;
            page.Response += (_, response) =>
            {
                if (!AllowedRequest(response.Url, response.Request.Method) || response.Request.ResourceType is not ("fetch" or "xhr")) return;
                lock (responses) { if (responses.Count < 30) responses.Add(response); else truncated = true; }
            };
            await page.GotoAsync(BrowserProbe.Provider.AbsoluteUri, new() { Timeout = 6000 });
            foreach (var step in workflow.Steps)
            {
                ct.ThrowIfCancellationRequested();
                var locator = page.Locator(step.Selector);
                switch (step.Action)
                {
                    case "fill": await locator.FillAsync(step.Value!); break;
                    case "click": await locator.ClickAsync(); break;
                    case "wait": await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible }); break;
                }
            }
            // The explicit completion selector guarantees the lab's search requests have completed.
            IResponse[] snapshot; lock (responses) snapshot = responses.ToArray();
            var result = new List<Fingerprint>();
            foreach (var response in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                var headers = await response.Request.AllHeadersAsync();
                var body = await response.TextAsync().WaitAsync(TimeSpan.FromSeconds(4), ct);
                if (body.Length > Discovery.MaxBody || (response.Request.PostData?.Length ?? 0) > Discovery.MaxBody) { truncated = true; continue; }
                result.Add(Discovery.FingerprintOf(new Exchange("browser", response.Request.Method, new Uri(response.Url).AbsolutePath,
                    new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase), response.Request.PostData ?? "", response.Status, body)));
            }
            return new(workflow.Name, result, truncated);
        }
        finally { gate.Release(); }
    }
    public static IReadOnlyList<Fingerprint> ImportHar(JsonElement har)
    {
        if (har.ValueKind != JsonValueKind.Object || !har.TryGetProperty("log", out var log) || log.ValueKind != JsonValueKind.Object || !log.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Expected HAR log.entries array.");
        if (entries.GetArrayLength() > 500) throw new ArgumentException("Maximum 500 HAR entries.");
        var result = new List<Fingerprint>();
        foreach (var entry in entries.EnumerateArray())
        {
            var request = Child(entry, "request"); var response = Child(entry, "response");
            var url = String(request, "url"); var method = String(request, "method");
            if (!AllowedRequest(url, method)) continue;
            var statusNode = Child(response, "status");
            if (statusNode.ValueKind != JsonValueKind.Number || !statusNode.TryGetInt32(out var status) || status is < 100 or > 599) continue;
            var post = String(Child(request, "postData"), "text"); var content = Child(response, "content"); var body = String(content, "text");
            if (String(content, "encoding") == "base64")
            {
                try { if (body.Length > Discovery.MaxBody * 2) continue; body = Encoding.UTF8.GetString(Convert.FromBase64String(body)); }
                catch (FormatException) { continue; }
            }
            if (post.Length > Discovery.MaxBody || body.Length > Discovery.MaxBody) continue;
            var headers = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var headerArray = Child(request, "headers");
            if (headerArray.ValueKind == JsonValueKind.Array)
                foreach (var item in headerArray.EnumerateArray()) { var name = String(item, "name"); if (name.Equals("content-type", StringComparison.OrdinalIgnoreCase) || name.Equals("x-demo-contract", StringComparison.OrdinalIgnoreCase)) headers[name] = String(item, "value"); }
            result.Add(Discovery.FingerprintOf(new Exchange("har", method, new Uri(url).AbsolutePath, headers, post, status, body)));
            if (result.Count >= 30) break;
        }
        return result;
    }
    private static JsonElement Child(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var child) ? child : default;
    private static string String(JsonElement node, string key) => Child(node, key) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";
}
