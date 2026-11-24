using System.Text.Json;
using BotIQ.Core;
using Microsoft.EntityFrameworkCore;

namespace BotIQ.Api;

public sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? BotId { get; set; }
    public string Kind { get; set; } = "";
    public string Outcome { get; set; } = "";
    public int DifferenceCount { get; set; }
    public int? BotStatus { get; set; }
    public int? BrowserStatus { get; set; }
    public string Details { get; set; } = "";
}

public sealed class HistoryDb(DbContextOptions<HistoryDb> options) : DbContext(options)
{
    public DbSet<HistoryEntry> Investigations => Set<HistoryEntry>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        var e = model.Entity<HistoryEntry>();
        e.ToTable("investigation_history"); e.HasKey(x => x.Id);
        e.Property(x => x.Kind).HasMaxLength(32); e.Property(x => x.Outcome).HasMaxLength(100);
        e.Property(x => x.Details).HasColumnType("text");
        e.HasIndex(x => x.CreatedAt); e.HasIndex(x => new { x.BotId, x.CreatedAt });
    }
}

// Only completed investigation summaries are persisted; raw bodies and uploaded code stay out of history.
public sealed class InvestigationHistory(IWebHostEnvironment env, IServiceProvider services, IConfiguration config)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public bool UsesPostgres => !string.IsNullOrWhiteSpace(config.GetConnectionString("BotIQ"));
    private string Root => Path.Combine(env.ContentRootPath, "App_Data", "history");
    public async Task InitializeAsync(CancellationToken ct)
    {
        if (!UsesPostgres) return;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HistoryDb>();
        // Version 001 is a transactional, idempotent SQL migration, not EnsureCreated.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(529351)", ct);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS botiq_schema_version (version integer PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());
            CREATE TABLE IF NOT EXISTS investigation_history (
                "Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "BotId" uuid NULL,
                "Kind" varchar(32) NOT NULL, "Outcome" varchar(100) NOT NULL, "DifferenceCount" integer NOT NULL,
                "BotStatus" integer NULL, "BrowserStatus" integer NULL, "Details" text NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_investigation_history_CreatedAt" ON investigation_history ("CreatedAt");
            CREATE INDEX IF NOT EXISTS "IX_investigation_history_BotId_CreatedAt" ON investigation_history ("BotId", "CreatedAt");
            INSERT INTO botiq_schema_version(version) VALUES (1) ON CONFLICT DO NOTHING;
            """, ct);
        await tx.CommitAsync(ct);
    }
    public Task RecordDemoAsync(Investigation report, Guid? botId, CancellationToken ct) => SaveAsync(new()
    {
        BotId = botId, Kind = "demo", CreatedAt = report.InvestigatedAt,
        Outcome = report.HistoricalVsLive.Count == 0 && report.BotVsLive.Count == 0 ? "No contract drift" : "Contract drift",
        DifferenceCount = report.HistoricalVsLive.Count, BotStatus = report.CurrentBot.Exchange.Status, BrowserStatus = report.LiveBrowser.Status,
        Details = string.Join("; ", report.HistoricalVsLive.Select(f => f.Area).Distinct())
    }, ct);
    public Task RecordDiscoveryAsync(MatchReport report, Guid? botId, CancellationToken ct) => SaveAsync(new()
    {
        BotId = botId, Kind = "discovery", Outcome = report.Outcome, DifferenceCount = report.Differences.Count,
        BrowserStatus = report.SelectedIndex is { } i ? report.Candidates.FirstOrDefault(c => c.Index == i)?.Request.Status : null,
        Details = string.Join("; ", report.Differences.Select(f => f.Area).Distinct())
    }, ct);
    public async Task SaveAsync(HistoryEntry entry, CancellationToken ct)
    {
        if (UsesPostgres)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HistoryDb>();
            db.Investigations.Add(entry); await db.SaveChangesAsync(ct); return;
        }
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Root);
            var path = Path.Combine(Root, entry.Id.ToString("N") + ".json");
            try { await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(entry, Json), ct); File.Move(path + ".tmp", path); }
            finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
            // Retain newest 1,000 local summaries; PostgreSQL has no automatic deletion.
            foreach (var file in Directory.EnumerateFiles(Root, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(1000)) File.Delete(file);
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<HistoryEntry>> ListAsync(int limit, Guid? botId, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 100);
        if (UsesPostgres)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HistoryDb>();
            var query = db.Investigations.AsNoTracking();
            if (botId.HasValue) query = query.Where(x => x.BotId == botId);
            return await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit).ToListAsync(ct);
        }
        await gate.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(Root)) return [];
            var list = new List<HistoryEntry>();
            foreach (var path in Directory.EnumerateFiles(Root, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                var entry = JsonSerializer.Deserialize<HistoryEntry>(await File.ReadAllTextAsync(path, ct), Json);
                if (entry is not null && (!botId.HasValue || entry.BotId == botId)) list.Add(entry);
            }
            return list.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(limit).ToArray();
        }
        finally { gate.Release(); }
    }
}
