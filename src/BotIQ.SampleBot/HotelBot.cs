using System.Net.Http.Json;
using System.Text.Json;
using BotIQ.Core;

namespace BotIQ.SampleBot;

// Deliberately models a legacy bot compiled against provider contract v1.
public sealed class HotelBot(HttpClient http)
{
    public async Task<BotExecution> ExecuteAsync(Scenario scenario, CancellationToken ct = default)
    {
        const string path = "/api/hotel/search";
        var body = JsonSerializer.Serialize(new { operationName = "HotelSearchV1", variables = new { hotelId = scenario.PropertyId, currency = scenario.Currency } });
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add("x-demo-contract", "1");
        using var response = await http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var exchange = new Exchange("deployed-bot-v1", "POST", path,
            new(StringComparer.OrdinalIgnoreCase) { ["content-type"] = "application/json", ["x-demo-contract"] = "1" },
            body, (int)response.StatusCode, responseBody);
        return new(exchange, response.IsSuccessStatusCode ? Parse(responseBody) : new(0, $"HTTP {(int)response.StatusCode}"));
    }

    public static ParseResult Parse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var rates = doc.RootElement.GetProperty("data").GetProperty("rates");
            var count = 0;
            foreach (var rate in rates.EnumerateArray())
            {
                _ = rate.GetProperty("provider").GetString();
                _ = rate.GetProperty("price").GetDecimal();
                _ = rate.GetProperty("translatedName").GetProperty("value").GetString();
                count++;
            }
            return new(count, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return new(0, $"Legacy parser: {ex.Message}"); }
    }
}
