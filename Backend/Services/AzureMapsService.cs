using System.Text.Json;
using EmergencyPlatform.Models;

namespace EmergencyPlatform.Services;

/// <summary>
/// Azure Maps – geocodes a text address to lat/lon.
/// Config keys: AzureMaps:SubscriptionKey
/// </summary>
public class AzureMapsService
{
    private readonly HttpClient _http;
    private readonly string _key;

    public AzureMapsService(IConfiguration config, IHttpClientFactory factory)
    {
        _http = factory.CreateClient();
        _key  = config["AzureMaps:SubscriptionKey"]!;
    }

    public async Task<LocationData> GeocodeAsync(string addressQuery)
    {
        var url = $"https://atlas.microsoft.com/search/address/json"
                + $"?api-version=1.0&subscription-key={_key}"
                + $"&query={Uri.EscapeDataString(addressQuery)}&limit=1";

        var resp = await _http.GetAsync(url);
        if (!resp.IsSuccessStatusCode)
            return new LocationData { InferredLocationDescription = addressQuery };

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var results = doc.RootElement.GetProperty("results");
        if (results.GetArrayLength() == 0)
            return new LocationData { InferredLocationDescription = addressQuery };

        var pos = results[0].GetProperty("position");
        return new LocationData
        {
            Lat = pos.GetProperty("lat").GetDouble(),
            Lon = pos.GetProperty("lon").GetDouble(),
            InferredLocationDescription = addressQuery
        };
    }
}
