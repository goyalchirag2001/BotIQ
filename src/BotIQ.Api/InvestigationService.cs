using System.Net.Http.Json;
using System.Text.Json;
using BotIQ.Core;
using BotIQ.SampleBot;

namespace BotIQ.Api;

public sealed class InvestigationService(HttpClient http, BrowserProbe browser, IWebHostEnvironment env)
{
    private readonly SemaphoreSlim gate = new(1,1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string BaselinePath => Path.Combine(env.ContentRootPath, "App_Data", "baseline.json");
    private static readonly Scenario DemoScenario = new();

    public async Task<int> VersionAsync(CancellationToken ct)
    {
        var state = await http.GetFromJsonAsync<ProviderState>("/api/state", ct);
        return state?.Version ?? throw new InvalidOperationException("Provider state unavailable.");
    }

    public async Task SetVersionAsync(int version, CancellationToken ct)
    {
        if (version is not (1 or 2)) throw new InvalidOperationException("Version must be 1 or 2.");
        await gate.WaitAsync(ct);
        try
        {
            using var response = await http.PostAsJsonAsync("/admin/version", new { version }, ct);
            response.EnsureSuccessStatusCode();
        }
        finally { gate.Release(); }
    }

    public async Task<Baseline?> GetBaselineAsync(CancellationToken ct)
    {
        if (!File.Exists(BaselinePath)) return null;
        return JsonSerializer.Deserialize<Baseline>(await File.ReadAllTextAsync(BaselinePath, ct), JsonOptions);
    }

    public async Task<Baseline> CaptureBaselineAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (await VersionAsync(ct) != 1) throw new InvalidOperationException("Restore provider v1 before capturing the known-good baseline.");
            var bot = await new HotelBot(http).ExecuteAsync(DemoScenario, ct);
            var capture = await browser.CaptureAsync(DemoScenario, ct);
            var browserOutput = HotelBot.Parse(capture.ResponseBody);
            if (bot.Output.Error != null || bot.Output.RateCount == 0 || capture.Status != 200 ||
                browserOutput.Error != null || browserOutput.RateCount != bot.Output.RateCount ||
                await VersionAsync(ct) != 1 || ContractComparer.Compare(bot.Exchange, capture).Count != 0)
                throw new InvalidOperationException("Baseline rejected: bot and browser must agree on the healthy v1 contract.");
            var baseline = new Baseline(DateTimeOffset.UtcNow, DemoScenario, capture, bot);
            Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)!);
            var temp = BaselinePath + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(baseline, JsonOptions), ct);
            File.Move(temp, BaselinePath, overwrite: true);
            return baseline;
        }
        finally { gate.Release(); }
    }

    public async Task<Investigation> InvestigateAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var baseline = await GetBaselineAsync(ct) ?? throw new InvalidOperationException("Capture a healthy baseline first.");
            var version = await VersionAsync(ct);
            var bot = await new HotelBot(http).ExecuteAsync(baseline.Scenario, ct);
            var live = await browser.CaptureAsync(baseline.Scenario, ct);
            if (await VersionAsync(ct) != version) throw new InvalidOperationException("Provider version changed during investigation; retry.");
            var historical = ContractComparer.Compare(baseline.Browser, live);
            var current = ContractComparer.Compare(bot.Exchange, live, compareResponse: false);
            var parser = live.Status is >= 200 and < 300 ? HotelBot.Parse(live.ResponseBody) : new(0, "Skipped: live response not successful");
            var hypotheses = new List<string>();
            if (bot.Exchange.Status >= 400 && live.Status is >= 200 and < 300 && current.Count > 0)
                hypotheses.Add("The deployed bot request is incompatible with the observed live request. Review HotelBot.ExecuteAsync (demo source mapping).");
            if (live.Status is >= 200 and < 300 && parser.Error is not null && historical.Any(f => f.Area == "Response schema"))
                hypotheses.Add("The old parser also fails against the successful live response. Review HotelBot.Parse (demo source mapping).");
            if (bot.Output.Error is null && bot.Output.RateCount == baseline.Bot.Output.RateCount && historical.Count == 0 && current.Count == 0)
                hypotheses.Add("No monitored contract or output regression found for this scenario.");
            if (hypotheses.Count == 0) hypotheses.Add("No specific cause established by the demo rules; inspect captured evidence.");
            return new(DateTimeOffset.UtcNow, baseline, bot, live, parser, historical, current, hypotheses);
        }
        finally { gate.Release(); }
    }

    private sealed record ProviderState(int Version);
}
