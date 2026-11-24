using System.Text.Json;

namespace BotIQ.Core;

public static class ContractComparer
{
    public static IReadOnlyList<Finding> Compare(Exchange before, Exchange after, bool compareResponse = true)
    {
        var result = new List<Finding>();
        Add("Request", "method", before.Method, after.Method, "High");
        Add("Request", "endpoint", before.Path, after.Path, "High");
        foreach (var key in before.Headers.Keys.Union(after.Headers.Keys, StringComparer.OrdinalIgnoreCase).Order())
            Add("Header", key, Header(before, key), Header(after, key), "Medium");
        CompareShape("POST data", before.RequestBody, after.RequestBody);
        Add("POST data", "$.operationName", ReadOperation(before.RequestBody), ReadOperation(after.RequestBody), "High");
        if (compareResponse)
        {
            Add("Response", "status", before.Status.ToString(), after.Status.ToString(), "High");
            // Error documents are not successful business response contracts.
            if (before.Status is >= 200 and < 300 && after.Status is >= 200 and < 300)
                CompareShape("Response schema", before.ResponseBody, after.ResponseBody);
        }
        return result;

        void Add(string area, string path, string oldValue, string newValue, string severity)
        {
            if (oldValue != newValue) result.Add(new(area, path, oldValue, newValue, severity));
        }
        void CompareShape(string area, string oldJson, string newJson)
        {
            var oldShape = Shape(oldJson);
            var newShape = Shape(newJson);
            foreach (var key in oldShape.Keys.Union(newShape.Keys).Order(StringComparer.Ordinal))
                Add(area, key, Format(oldShape, key), Format(newShape, key), "High");
        }
    }

    private static string Header(Exchange x, string key) => x.Headers
        .FirstOrDefault(h => string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? "(missing)";

    private static string ReadOperation(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("operationName", out var op) && op.ValueKind == JsonValueKind.String
                ? op.GetString() ?? "(null)" : "(missing)";
        }
        catch (JsonException) { return "(non-JSON)"; }
    }

    private static string Format(Dictionary<string, SortedSet<string>> shape, string key) =>
        shape.TryGetValue(key, out var types) ? string.Join(" | ", types) : "(missing)";

    private static Dictionary<string, SortedSet<string>> Shape(string json)
    {
        var shape = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, "$", shape);
        }
        catch (JsonException) { shape["$"] = new(StringComparer.Ordinal) { "NonJson" }; }
        return shape;
    }

    private static void Walk(JsonElement element, string path, Dictionary<string, SortedSet<string>> shape)
    {
        if (!shape.TryGetValue(path, out var types)) shape[path] = types = new(StringComparer.Ordinal);
        types.Add(element.ValueKind switch { JsonValueKind.True or JsonValueKind.False => "Boolean", _ => element.ValueKind.ToString() });
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                Walk(property.Value, path + "." + property.Name.Replace("\\", "\\\\").Replace(".", "\\."), shape);
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) Walk(item, path + "[*]", shape);
    }
}
