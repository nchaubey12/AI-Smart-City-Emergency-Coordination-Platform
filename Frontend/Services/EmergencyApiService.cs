using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EmergencyPlatform.Frontend.Models;

namespace EmergencyPlatform.Frontend.Services;

/// <summary>
/// Blazor client-side service that calls the Emergency API backend.
/// Inject this as a scoped service in Program.cs.
/// </summary>
public class EmergencyApiService
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public EmergencyApiService(HttpClient http) => _http = http;

    // ── Text Report ───────────────────────────────────────────────────────

    public async Task<IncidentReportViewModel?> SubmitTextAsync(
        string text, double? lat = null, double? lon = null)
    {
        var payload = new { text, lat, lon };
        var resp    = await _http.PostAsJsonAsync("api/emergency/text", payload);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IncidentReportViewModel>(_jsonOpts);
    }

    // ── Image Report (base64) ────────────────────────────────────────────

    public async Task<IncidentReportViewModel?> SubmitImageBase64Async(
        string base64, string mimeType = "image/jpeg",
        double? lat = null, double? lon = null)
    {
        var payload = new { base64Image = base64, mimeType, lat, lon };
        var resp    = await _http.PostAsJsonAsync("api/emergency/image", payload);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IncidentReportViewModel>(_jsonOpts);
    }

    // ── Image Report (file stream) ───────────────────────────────────────

    public async Task<IncidentReportViewModel?> SubmitImageFileAsync(
        Stream imageStream, string fileName, string contentType,
        double? lat = null, double? lon = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(imageStream) { Headers = { ContentType = new(contentType) } },
            "file", fileName);
        if (lat.HasValue) form.Add(new StringContent(lat.Value.ToString()), "lat");
        if (lon.HasValue) form.Add(new StringContent(lon.Value.ToString()), "lon");

        var resp = await _http.PostAsync("api/emergency/image/upload", form);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IncidentReportViewModel>(_jsonOpts);
    }

    // ── Audio Report ─────────────────────────────────────────────────────

    public async Task<IncidentReportViewModel?> SubmitAudioAsync(
        string base64Audio, string mimeType = "audio/wav",
        double? lat = null, double? lon = null)
    {
        var payload = new { base64Audio, mimeType, lat, lon };
        var resp    = await _http.PostAsJsonAsync("api/emergency/audio", payload);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IncidentReportViewModel>(_jsonOpts);
    }
}
