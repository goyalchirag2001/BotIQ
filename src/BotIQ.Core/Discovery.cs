using System.Text.Json;

namespace BotIQ.Core;

public sealed record Fingerprint(string Method, string Path, int Status, string? Operation,
    Dictionary<string, string> Headers, Dictionary<string, string> RequestShape, Dictionary<string, string> ResponseShape, bool Incomplete = false);
public sealed record RankedCandidate(int Index, Fingerprint Request, int Score, string[] Reasons);
public sealed record MatchReport(string Outcome, int? SelectedIndex, IReadOnlyList<RankedCandidate> Candidates, IReadOnlyList<Finding> Differences);

public static class Discovery
{
    public const int MaxBody = 16_000_000;
    public const int MaxShapePaths = 5000;
    public static Fingerprint FingerprintOf(Exchange exchange)
    {
        var request = Shape(exchange.RequestBody, out var requestIncomplete);
        var responseIncomplete = false;
        var response = exchange.Status is >= 200 and < 300 ? Shape(exchange.ResponseBody, out responseIncomplete) : new Dictionary<string, string>();
        string? operation = null;
        try
        {
            using var doc = JsonDocument.Parse(exchange.RequestBody);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("operationName", out var op) && op.ValueKind == JsonValueKind.String)
            {
                var value = op.GetString();
                if (value is { Length: > 0 and <= 100 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) operation = value;
            }
        }
        catch (JsonException) { }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in exchange.Headers)
        {
            if (key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
            {
                var type = value.Split(';')[0].Trim().ToLowerInvariant();
                headers["content-type"] = type is "application/json" or "application/graphql" or "application/x-www-form-urlencoded" ? type : "other";
            }
            if (key.Equals("x-demo-contract", StringComparison.OrdinalIgnoreCase)) headers["x-demo-contract"] = value is "1" or "2" ? value : "other";
        }
        return new(exchange.Method.ToUpperInvariant(), exchange.Path.Split('?')[0], exchange.Status, operation, headers, request, response, requestIncomplete || responseIncomplete);
    }

    public static MatchReport Match(Fingerprint baseline, IReadOnlyList<Fingerprint> candidates)
    {
        if (baseline.Incomplete) throw new ArgumentException("Baseline schema is incomplete; choose a complete fingerprint.");
        if (baseline.Status is < 200 or >= 300) throw new ArgumentException("Select a successful baseline request.");
        var ranked = candidates.Select((candidate, index) => (candidate, index)).Where(x => !x.candidate.Incomplete).Select(x => Rank(baseline, x.candidate, x.index))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Index).ToArray();
        if (ranked.Length == 0 || ranked[0].Score < 45) return new("No confident match", null, ranked, []);
        if (ranked.Length > 1 && ranked[0].Score - ranked[1].Score < 10) return new("Ambiguous", null, ranked, []);
        return new("Candidate match", ranked[0].Index, ranked, Compare(baseline, ranked[0].Request));
    }

    private static RankedCandidate Rank(Fingerprint a, Fingerprint b, int index)
    {
        var reasons = new List<string>(); int score = 0;
        void Add(int points, string reason) { if (points > 0) { score += points; reasons.Add($"{reason} (+{points})"); } }
        Add(a.Method == b.Method ? 15 : 0, "HTTP method");
        Add(a.Path == b.Path ? 15 : 0, "Endpoint");
        Add(a.Operation != null && a.Operation == b.Operation ? 10 : 0, "Operation");
        Add(Similarity(a.RequestShape, b.RequestShape, 20), "Request paths");
        Add(Similarity(a.ResponseShape, b.ResponseShape, 40), "Response paths");
        return new(index, b, score, reasons.ToArray());
    }
    private static int Similarity(Dictionary<string, string> a, Dictionary<string, string> b, int weight)
    {
        var left = a.Keys.Where(k => k != "$").ToHashSet(StringComparer.Ordinal);
        var right = b.Keys.Where(k => k != "$").ToHashSet(StringComparer.Ordinal);
        var total = left.Union(right).Count();
        return total == 0 ? 0 : (int)Math.Round(weight * (double)left.Intersect(right).Count() / total);
    }
    private static IReadOnlyList<Finding> Compare(Fingerprint a, Fingerprint b)
    {
        var findings = new List<Finding>();
        void Add(string area, string path, string? before, string? after) { if (before != after) findings.Add(new(area, path, before ?? "(missing)", after ?? "(missing)", "Review")); }
        void Diff(string area, Dictionary<string,string> left, Dictionary<string,string> right)
        {
            foreach (var key in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
                Add(area, key, left.GetValueOrDefault(key), right.GetValueOrDefault(key));
        }
        Add("Request", "method", a.Method, b.Method); Add("Request", "endpoint", a.Path, b.Path);
        Add("POST data", "$.operationName", a.Operation, b.Operation); Diff("Header", a.Headers, b.Headers);
        Diff("POST data", a.RequestShape, b.RequestShape);
        Add("Response", "status", a.Status.ToString(), b.Status.ToString());
        if (b.Status is >= 200 and < 300 && a.ResponseShape.Count > 0 && b.ResponseShape.Count > 0) Diff("Response schema", a.ResponseShape, b.ResponseShape);
        return findings;
    }
    private static Dictionary<string, string> Shape(string json, out bool incomplete)
    {
        incomplete = false;
        if (string.IsNullOrWhiteSpace(json)) return new();
        if (json.Length > MaxBody) { incomplete = true; return new(); }
        bool limited = false;
        var paths = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        int visited = 0;
        try { using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 }); Walk(doc.RootElement, "$"); }
        catch (JsonException) { incomplete = true; return new(); }
        incomplete = limited;
        return paths.ToDictionary(x => x.Key, x => string.Join(" | ", x.Value), StringComparer.Ordinal);
        void Walk(JsonElement element, string path)
        {
            if (++visited > 500_000 || path.Length > 500 || (paths.Count >= MaxShapePaths && !paths.ContainsKey(path))) { limited = true; return; }
            if (!paths.TryGetValue(path, out var types)) paths[path] = types = new(StringComparer.Ordinal);
            types.Add(element.ValueKind is JsonValueKind.True or JsonValueKind.False ? "Boolean" : element.ValueKind.ToString());
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var p in element.EnumerateObject()) { if (visited >= 500_000) { limited = true; break; } Walk(p.Value, path + "." + p.Name.Replace("\\", "\\\\").Replace(".", "\\.")); }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) { if (visited >= 500_000) { limited = true; break; } Walk(item, path + "[*]"); }
        }
    }
}
