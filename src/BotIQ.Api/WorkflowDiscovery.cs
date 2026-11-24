using System.Text;
using System.Text.Json;
using BotIQ.Core;


namespace BotIQ.Api;

public sealed record BrowserStep(string Action, string Selector, string? Value);
public sealed record BrowserWorkflow(string Name, IReadOnlyList<BrowserStep> Steps);
public sealed record CaptureReport(string Workflow, IReadOnlyList<Fingerprint> Requests, bool Truncated, IReadOnlyList<string> Diagnostics);
public sealed record DiscoveryCompare(Fingerprint Baseline, BrowserWorkflow Workflow, Guid? BotId = null);

public sealed class WorkflowDiscovery(BrowserProbe browser)
{
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
        var raw = await browser.CaptureWorkflowAsync(workflow, ct);
        var requests = raw.Exchanges.Select(Discovery.FingerprintOf).ToArray();
        return new(workflow.Name, requests, raw.Truncated || requests.Any(r => r.Incomplete), raw.Diagnostics);
    }
    public static IReadOnlyList<Fingerprint> ImportHar(JsonElement har)
    {
        if (har.ValueKind != JsonValueKind.Object || !har.TryGetProperty("log", out var log) || log.ValueKind != JsonValueKind.Object || !log.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Expected HAR log.entries array.");
        if (entries.GetArrayLength() > 20_000) throw new ArgumentException("Maximum 20,000 HAR entries.");
        var result = new List<Fingerprint>();
        long totalPaths = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            var request = Child(entry, "request"); var response = Child(entry, "response");
            var url = String(request, "url"); var method = String(request, "method");
            if (!AllowedRequest(url, method)) continue;
            var statusNode = Child(response, "status");
            if (statusNode.ValueKind != JsonValueKind.Number || !statusNode.TryGetInt32(out var status) || status is < 100 or > 599) continue;
            var post = String(Child(request, "postData"), "text"); var content = Child(response, "content"); var body = String(content, "text");
            bool missingBody = status is >= 200 and < 300 && Child(content, "text").ValueKind != JsonValueKind.String;
            if (String(content, "encoding") == "base64")
            {
                try { if (body.Length > Discovery.MaxBody * 2) { missingBody = true; body = ""; } else body = Encoding.UTF8.GetString(Convert.FromBase64String(body)); }
                catch (FormatException) { missingBody = true; body = ""; }
            }
            if (post.Length > Discovery.MaxBody || body.Length > Discovery.MaxBody)
            {
                result.Add(new(method, new Uri(url).AbsolutePath, status, null, new(), new(), new(), true));
                if (result.Count >= 500) break;
                continue;
            }
            var headers = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var headerArray = Child(request, "headers");
            if (headerArray.ValueKind == JsonValueKind.Array)
                foreach (var item in headerArray.EnumerateArray()) { var name = String(item, "name"); if (name.Equals("content-type", StringComparison.OrdinalIgnoreCase) || name.Equals("x-demo-contract", StringComparison.OrdinalIgnoreCase)) headers[name] = String(item, "value"); }
            var fingerprint = Discovery.FingerprintOf(new Exchange("har", method, new Uri(url).AbsolutePath, headers, post, status, body));
            totalPaths += fingerprint.RequestShape.Count + fingerprint.ResponseShape.Count;
            if (totalPaths > 100_000) throw new ArgumentException("HAR contains more than 100,000 schema paths across eligible entries. Split it into smaller captures.");
            result.Add(fingerprint with { Incomplete = fingerprint.Incomplete || missingBody });
            if (result.Count >= 500) break;
        }
        return result;
    }
    private static JsonElement Child(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var child) ? child : default;
    private static string String(JsonElement node, string key) => Child(node, key) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";
}
