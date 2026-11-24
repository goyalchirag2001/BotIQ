using BotIQ.Core;
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
Console.WriteLine("13 assertions passed.");
static void Check(bool ok,string name){if(!ok)throw new Exception($"FAIL: {name}");Console.WriteLine($"PASS: {name}");}
