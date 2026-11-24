using BotIQ.Core;
using Microsoft.Playwright;

namespace BotIQ.Api;

public sealed class BrowserProbe
{
    public static readonly Uri Provider = new("http://127.0.0.1:5071/");

    public async Task<Exchange> CaptureAsync(Scenario scenario)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block });
        // Only our synthetic provider is permitted. No arbitrary target URLs or credentials.
        await context.RouteAsync("**/*", route =>
        {
            var uri = new Uri(route.Request.Url);
            return uri.Scheme == Provider.Scheme && uri.Host == Provider.Host && uri.Port == Provider.Port
                ? route.ContinueAsync() : route.AbortAsync();
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15000);
        await page.GotoAsync(Provider.AbsoluteUri);
        await page.Locator("#property").FillAsync(scenario.PropertyId);
        // The recorded workflow identifies the search action, not a particular API path.
        // For this provider there is one POST fetch/XHR per search; real sites need candidate ranking.
        var response = await page.RunAndWaitForResponseAsync(
            () => page.Locator("#search").ClickAsync(),
            r => r.Request.Method == "POST" && (r.Request.ResourceType is "fetch" or "xhr") &&
                 new Uri(r.Url).GetLeftPart(UriPartial.Authority) == Provider.GetLeftPart(UriPartial.Authority));
        var headers = await response.Request.AllHeadersAsync();
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "content-type", "x-demo-contract" })
            if (headers.TryGetValue(name, out var value))
                selected[name] = name == "content-type" ? value.Split(';')[0].Trim() : value;
        return new Exchange("independent-browser", response.Request.Method, new Uri(response.Url).PathAndQuery,
            selected, response.Request.PostData ?? "", response.Status, await response.TextAsync());
    }
}
