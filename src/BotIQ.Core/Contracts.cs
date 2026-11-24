namespace BotIQ.Core;

public sealed record Scenario(string PropertyId = "HOTEL-101", string Currency = "USD");
public sealed record Exchange(string Source, string Method, string Path,
    Dictionary<string, string> Headers, string RequestBody, int Status, string ResponseBody);
public sealed record ParseResult(int RateCount, string? Error);
public sealed record BotExecution(Exchange Exchange, ParseResult Output);
public sealed record Baseline(DateTimeOffset CapturedAt, Scenario Scenario, Exchange Browser, BotExecution Bot);
public sealed record Finding(string Area, string Path, string Before, string After, string Severity);
public sealed record Investigation(DateTimeOffset InvestigatedAt, Baseline Baseline, BotExecution CurrentBot,
    Exchange LiveBrowser, ParseResult LiveResponseWithOldParser, IReadOnlyList<Finding> HistoricalVsLive,
    IReadOnlyList<Finding> BotVsLive, IReadOnlyList<string> Hypotheses);
