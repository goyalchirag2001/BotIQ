using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BotIQ.CodeIntelligence;

namespace BotIQ.Api;

public sealed record RegisterBot(string Name, string Version, IReadOnlyList<SourceFile> Files);
public sealed record BotSnapshot(Guid Id, string Name, string Version, DateTimeOffset CreatedAt, string SourceHash, SourceReport Analysis);
public sealed record StoredBot(BotSnapshot Snapshot, IReadOnlyList<SourceFile> Files);
public sealed class BotRegistry(IWebHostEnvironment env)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Root => Path.Combine(env.ContentRootPath, "App_Data", "bots");

    public async Task<BotSnapshot> RegisterAsync(RegisterBot input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100 || string.IsNullOrWhiteSpace(input.Version) || input.Version.Length > 50)
            throw new ArgumentException("Bot name and version are required (100 / 50 characters max).");
        if (input.Files is null || input.Files.Count is < 1 or > 100) throw new ArgumentException("Upload between 1 and 100 C# files.");
        if (input.Files.Any(f => f is null || string.IsNullOrWhiteSpace(f.Name) || f.Name.Length > 200 || !f.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.Content is null || f.Content.Length > 2_000_000))
            throw new ArgumentException("Each file must be a .cs source file with at most 2,000,000 characters.");
        var files = input.Files.Select(f => new SourceFile(f.Name.Replace('\\', '/'), f.Content)).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        if (files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length || files.Any(f => f.Name.StartsWith('/') || f.Name.Split('/').Any(p => p is "" or "." or "..") || f.Name.Contains(':') || f.Name.Any(char.IsControl)))
            throw new ArgumentException("Use unique relative source file names; absolute paths are not accepted.");
        if (files.Sum(f => Encoding.UTF8.GetByteCount(f.Content)) > 8_388_608) throw new ArgumentException("Combined source is limited to 8 MiB UTF-8.");
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Root);
            if (Directory.EnumerateFiles(Root, "*.json").Take(100).Count() >= 100) throw new ArgumentException("Local registry limit of 100 snapshots reached.");
            var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(files, Json)));
            var report = SourceAnalyzer.Analyze(files);
            var snapshot = new BotSnapshot(Guid.NewGuid(), input.Name.Trim(), input.Version.Trim(), DateTimeOffset.UtcNow, hash, report);
            var path = PathFor(snapshot.Id);
            try
            {
                await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new StoredBot(snapshot, files), Json), ct);
                File.Move(path + ".tmp", path);
            }
            finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
            return snapshot;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<object>> ListAsync(CancellationToken ct)
    {
        if (!Directory.Exists(Root)) return [];
        var result = new List<BotSnapshot>();
        foreach (var file in Directory.EnumerateFiles(Root, "*.json"))
        {
            var bot = JsonSerializer.Deserialize<StoredBot>(await File.ReadAllTextAsync(file, ct), Json);
            if (bot is not null) result.Add(bot.Snapshot);
        }
        return result.OrderByDescending(b => b.CreatedAt).Select(b => (object)new { b.Id, b.Name, b.Version, b.CreatedAt, b.SourceHash, fileCount = b.Analysis.FileCount, evidenceCount = b.Analysis.Evidence.Count }).ToArray();
    }

    public async Task<BotSnapshot?> GetAsync(Guid id, CancellationToken ct)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<StoredBot>(await File.ReadAllTextAsync(path, ct), Json)?.Snapshot;
    }
    public async Task<DependencyReport> DependenciesAsync(Guid id, CancellationToken ct)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) throw new ArgumentException("Source snapshot not found.");
        var stored = JsonSerializer.Deserialize<StoredBot>(await File.ReadAllTextAsync(path, ct), Json) ?? throw new ArgumentException("Invalid source snapshot.");
        return DependencyAnalyzer.Analyze(stored.Files);
    }
    private string PathFor(Guid id) => Path.Combine(Root, id.ToString("N") + ".json");
}
