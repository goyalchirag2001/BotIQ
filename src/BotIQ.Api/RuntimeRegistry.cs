using System.Text.Json;
using BotIQ.Core;

namespace BotIQ.Api;

public sealed record RuntimeHit(int Sequence, string Method, string Path, int Status, int DurationMs, int? DependsOnSequence = null);
public sealed record RuntimeTrace(IReadOnlyList<RuntimeHit> Hits, int OutputRows, IReadOnlyList<string> OutputColumns, bool ParsingFailed);
public sealed record StoredTrace(Guid Id, DateTimeOffset CreatedAt, RuntimeTrace Trace);
public sealed record DiscoveryProfile(string Name, BrowserWorkflow Workflow, Fingerprint Baseline);
public sealed record StoredProfile(Guid Id, DateTimeOffset CreatedAt, DiscoveryProfile Profile);

// Local evidence only. Traces are submitted by a trusted lab caller; no arbitrary DLL execution.
public sealed class RuntimeRegistry(IWebHostEnvironment env, BotRegistry bots)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<StoredTrace> SaveAsync(Guid id, RuntimeTrace trace, CancellationToken ct)
    {
        if (trace.Hits is null || trace.Hits.Count is < 1 or > 100 || trace.OutputRows is < 0 or > 10_000_000 || trace.OutputColumns is null || trace.OutputColumns.Count > 200 || trace.OutputColumns.Any(c => !Identifier(c, 128))) throw new ArgumentException("Invalid trace counts or output column names.");
        var seen = new HashSet<int>();
        foreach (var hit in trace.Hits)
        {
            if (hit is null || hit.Sequence <= 0 || seen.Contains(hit.Sequence) || (hit.DependsOnSequence is { } prior && !seen.Contains(prior)) || hit.Method is not ("GET" or "POST") || !SafePath(hit.Path) || hit.Status is < 100 or > 599 || hit.DurationMs is < 0 or > 600_000) throw new ArgumentException("Invalid runtime hit. Use ordered unique sequences, earlier dependencies and synthetic API paths without query strings.");
            seen.Add(hit.Sequence);
        }
        var value = new StoredTrace(Guid.NewGuid(), DateTimeOffset.UtcNow, trace);
        await Save(id, "runtime", value.Id, value, ct); return value;
    }
    public async Task<StoredProfile> SaveProfileAsync(Guid id, DiscoveryProfile profile, CancellationToken ct)
    {
        if (!Identifier(profile.Name, 100, true)) throw new ArgumentException("Invalid profile name.");
        WorkflowDiscovery.Validate(profile.Workflow);
        var f = profile.Baseline;
        if (f is null || f.Incomplete || !SafePath(f.Path) || f.Method is not ("GET" or "POST") || f.Status is < 200 or >= 300 || f.Headers is null || f.Headers.Count > 2 || f.Headers.Any(h => h.Key switch { "content-type" => h.Value is not ("application/json" or "application/graphql" or "application/x-www-form-urlencoded" or "other"), "x-demo-contract" => h.Value is not ("1" or "2" or "other"), _ => true }) || (f.Operation is not null && !Identifier(f.Operation, 100)) || !ValidShape(f.RequestShape) || !ValidShape(f.ResponseShape)) throw new ArgumentException("Select a complete sanitized lab fingerprint.");
        var value = new StoredProfile(Guid.NewGuid(), DateTimeOffset.UtcNow, profile);
        await Save(id, "profiles", value.Id, value, ct); return value;
    }
    public Task<IReadOnlyList<StoredTrace>> ListAsync(Guid id, CancellationToken ct) => List<StoredTrace>(id, "runtime", ct);
    public Task<IReadOnlyList<StoredProfile>> ListProfilesAsync(Guid id, CancellationToken ct) => List<StoredProfile>(id, "profiles", ct);
    private async Task<string> DirectoryFor(Guid id, string kind, CancellationToken ct)
    {
        if (await bots.GetAsync(id, ct) is null) throw new ArgumentException("Bot snapshot not found.");
        return Path.Combine(env.ContentRootPath, "App_Data", kind, id.ToString("N"));
    }
    private async Task Save<T>(Guid id, string kind, Guid itemId, T item, CancellationToken ct)
    {
        var dir = await DirectoryFor(id, kind, ct);
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(dir);
            if (Directory.EnumerateFiles(dir, "*.json").Take(100).Count() >= 100) throw new ArgumentException("Maximum 100 records per snapshot and evidence type.");
            var path = Path.Combine(dir, itemId.ToString("N") + ".json");
            try { await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(item, Json), ct); File.Move(path + ".tmp", path); }
            finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
        }
        finally { gate.Release(); }
    }
    private async Task<IReadOnlyList<T>> List<T>(Guid id, string kind, CancellationToken ct) where T : class
    {
        var dir = await DirectoryFor(id, kind, ct); var result = new List<T>();
        if (!Directory.Exists(dir)) return result;
        foreach (var path in Directory.EnumerateFiles(dir, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(100))
            if (JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json) is { } value) result.Add(value);
        return result;
    }
    private static bool Identifier(string? s, int limit, bool spaces = false) => s is { Length: > 0 } && s.Length <= limit && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' || (spaces && c == ' '));
    private static bool SafePath(string? s) => s is { Length: > 5 and <= 500 } && s.StartsWith("/api/", StringComparison.Ordinal) && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.');
    private static bool ValidShape(Dictionary<string,string>? shape) => shape is not null && shape.Count <= Discovery.MaxShapePaths && shape.All(x => x.Key.Length <= 500 && x.Key.StartsWith('$') && x.Value is not null && x.Value.Split(" | ").All(t => t is "Object" or "Array" or "String" or "Number" or "Null" or "Boolean"));
}
