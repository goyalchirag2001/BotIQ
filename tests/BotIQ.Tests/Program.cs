using BotIQ.Core;
using BotIQ.CodeIntelligence;
using BotIQ.SampleBot;

var v1 = new Exchange("baseline", "POST", "/api/hotel/search", new() { ["content-type"] = "application/json", ["x-demo-contract"] = "1" },
    """{"operationName":"HotelSearchV1","variables":{"hotelId":"HOTEL-101","currency":"USD"}}""", 200,
    """{"data":{"rates":[{"provider":"DemoStay","price":120,"translatedName":{"value":"Free cancellation"}}]}}""");
var v2 = new Exchange("live", "POST", "/api/v2/availability", new() { ["Content-Type"] = "application/json", ["x-demo-contract"] = "2" },
    """{"operationName":"PropertyAvailability","variables":{"propertyId":"HOTEL-101","currency":"USD"}}""", 200,
    """{"data":{"rates":[{"provider":"DemoStay","price":120,"translatedName":null},{"provider":"Other","price":130,"translatedName":{"value":"Room only"}}]}}""");
var diff = ContractComparer.Compare(v1,v2);
Check(diff.Any(x => x.Path == "endpoint"), "endpoint change");
Check(diff.Any(x => x.Path == "x-demo-contract"), "header change");
Check(!diff.Any(x => x.Path.Equals("content-type", StringComparison.OrdinalIgnoreCase)), "header case ignored");
Check(diff.Any(x => x.Path == "$.operationName" && x.After == "PropertyAvailability"), "operation change");
Check(diff.Any(x => x.Path == "$.variables.hotelId" && x.After == "(missing)"), "removed parameter");
Check(diff.Any(x => x.Path == "$.variables.propertyId" && x.Before == "(missing)"), "new parameter");
Check(diff.Any(x => x.Path == "$.data.rates[*].translatedName" && x.After.Contains("Null")), "null type added across array elements");
Check(ContractComparer.Compare(v1,v1 with {ResponseBody=v1.ResponseBody.Replace("120", "999")}).Count == 0, "changing prices does not change schema");
Check(ContractComparer.Compare(v1,v1).Count == 0, "identical contracts");
Check(!ContractComparer.Compare(v1,v2 with {Status=404}).Any(x=>x.Area=="Response schema"), "error response not business schema");
Check(!ContractComparer.Compare(v1,v2, false).Any(x=>x.Area.StartsWith("Response")), "request-only mode");
Check(HotelBot.Parse(v1.ResponseBody).RateCount==1, "old parser accepts v1");
Check(HotelBot.Parse(v2.ResponseBody).Error is not null, "old parser rejects nullable v2 contract");
var report = SourceAnalyzer.Analyze([new("HotelBot.cs", """
class HotelBot {
  void ExecuteAsync() {
    const string path = "/api/hotel/search";
    var body = new { operationName = "HotelSearchV1", variables = new { hotelId = "H-1" } };
    headers.Add("x-demo-contract", "1");
    http.SendAsync(request);
  }
  void Parse() {
    var translated = rate.GetProperty("translatedName").GetProperty("value");
    var optional = deal?["translatedName"];
  }
}
""")]);
Check(report.Diagnostics.Count == 0, "Roslyn accepts syntactically valid source without resolving dependencies");
Check(report.Evidence.Any(e => e.Kind == "EndpointLiteral" && e.Key == "/api/hotel/search" && e.Line == 3), "endpoint with actual line number");
Check(report.Evidence.Any(e => e.Kind == "Operation" && e.Key == "HotelSearchV1"), "operation literal");
Check(report.Evidence.Any(e => e.Kind == "Header" && e.Key == "x-demo-contract"), "header key");
Check(report.Evidence.Any(e => e.Kind == "PayloadMember" && e.Key == "hotelId"), "payload member");
Check(report.Evidence.Any(e => e.Kind == "HttpCall" && e.Key == "SendAsync"), "HTTP call candidate");
Check(report.Evidence.Any(e => e.Kind == "ParserProperty" && e.Key == "translatedName" && e.Method == "Parse"), "JSON parser property");
Check(report.Evidence.Any(e => e.Kind == "IndexerKey" && e.Key == "translatedName"), "conditional indexer");
var impact = SourceAnalyzer.MapImpact(report, diff);
Check(impact.Any(e => e.Path == "endpoint" && e.Confidence == "Exact literal"), "endpoint finding maps to source");
Check(impact.Any(e => e.Path == "$.operationName"), "operation finding maps to source");
Check(impact.Any(e => e.Path == "$.variables.hotelId"), "payload finding maps to source");
Check(impact.Any(e => e.Path == "$.data.rates[*].translatedName" && e.Location.Method == "Parse" && e.Confidence == "Heuristic"), "schema finding maps to candidate parser");
Check(SourceAnalyzer.Analyze([new("Broken.cs", "class {")]).Diagnostics.Count > 0, "syntax diagnostics recorded");
Check(SourceAnalyzer.MapImpact(report, [new("Request", "endpoint", "/unrelated", "/new", "High")]).Count == 0, "unrelated endpoint does not map");
var legacy = SourceAnalyzer.Analyze([new("Legacy.cs", """
class Legacy { void Send() {
    string strPostData = "{\"operationName\":\"OldSearch\",\"hotelId\":\"" + id + "\"}";
} }
""")]);
Check(legacy.Evidence.Any(e => e.Kind == "Operation" && e.Key == "OldSearch"), "legacy concatenated POST operation");
Check(legacy.Evidence.Any(e => e.Kind == "PayloadMember" && e.Key == "hotelId"), "legacy concatenated POST key");
var baselineFingerprint = Discovery.FingerprintOf(v1 with { Headers = new() { ["content-type"] = "application/json", ["x-demo-contract"] = "1", ["authorization"] = "secret" }, Path = "/api/hotel/search?token=secret" });
var nextFingerprint = Discovery.FingerprintOf(v2);
var ping = Discovery.FingerprintOf(new Exchange("browser", "GET", "/api/ping", new(), "", 200, "{\"ok\":true}"));
Check(baselineFingerprint.Path == "/api/hotel/search", "fingerprint omits query values");
Check(!baselineFingerprint.Headers.ContainsKey("authorization"), "fingerprint drops secret headers");
Check(!baselineFingerprint.RequestShape.Values.Contains("HOTEL-101"), "fingerprint drops scalar values");
var match = Discovery.Match(baselineFingerprint, [ping, nextFingerprint, ping]);
Check(match.SelectedIndex == 1, "replacement ranked above unrelated traffic");
Check(match.Differences.Any(x => x.Path == "endpoint"), "replacement endpoint diff");
Check(match.Differences.Any(x => x.Path == "$.operationName"), "replacement operation diff");
Check(match.Differences.Any(x => x.Path == "$.data.rates[*].translatedName"), "replacement schema diff");
Check(Discovery.Match(baselineFingerprint, [nextFingerprint, nextFingerprint]).Outcome == "Ambiguous", "tie is not auto-selected");
Check(Discovery.Match(baselineFingerprint, [ping]).SelectedIndex is null, "weak match rejected");
Check(Discovery.Match(baselineFingerprint, []).SelectedIndex is null, "empty capture rejected");
var large = Discovery.FingerprintOf(v1 with { ResponseBody = "{\"padding\":\"" + new string('x', 300_000) + "\",\"data\":{\"rate\":1}}" });
Check(!large.Incomplete && large.ResponseShape.ContainsKey("$.data.rate"), "large JSON beyond previous character limit");
var oversized = Discovery.FingerprintOf(v1 with { ResponseBody = new string('x', Discovery.MaxBody + 1) });
Check(oversized.Incomplete, "oversized body marked incomplete");
Check(Discovery.Match(baselineFingerprint, [nextFingerprint with { Incomplete = true }]).SelectedIndex is null, "incomplete candidate rejected");
var dependencies = DependencyAnalyzer.Analyze([new("Legacy.cs", """
class Legacy {
 void Run() { var completeUrl = baseUrl + path; var strPostData = Build(id); var strResponse = HttpPost(completeUrl,strPostData,headers); var token = Parse(strResponse); drRoomType["HotelID"] = token; }
 string Build(string id) => id;
 string Parse(string response) => response;
}
""")]);
Check(dependencies.Methods.Count == 3, "method inventory");
Check(dependencies.Calls.Any(x => x.To.EndsWith(":Build")), "cross-method candidate edge");
Check(dependencies.DataLinks.Any(x => x.Kind == "URL construction" && x.Inputs.Contains("baseUrl")), "dynamic URL dependencies");
Check(dependencies.DataLinks.Any(x => x.Kind == "HTTP invocation" && x.Inputs.Contains("strPostData")), "HTTP argument dependency");
Check(dependencies.OutputColumns.Any(x => x.Name == "HotelID"), "DataTable output candidate");
Console.WriteLine("47 assertions passed.");
static void Check(bool ok,string name){if(!ok)throw new Exception($"FAIL: {name}");Console.WriteLine($"PASS: {name}");}
