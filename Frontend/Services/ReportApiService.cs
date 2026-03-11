using System.Net.Http.Json;
using System.Text.Json;
using EmergencyPlatform.Frontend.Models;

namespace EmergencyPlatform.Frontend.Services;

public class ReportApiService
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    public ReportApiService(HttpClient http) => _http = http;

    public async Task<SubmitReportResponse> SubmitAsync(SubmitReportRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/reports/submit", req);
            return await resp.Content.ReadFromJsonAsync<SubmitReportResponse>(_opts)
                   ?? new SubmitReportResponse { Success = false, Message = "No response." };
        }
        catch (Exception ex)
        {
            return new SubmitReportResponse { Success = false, Message = $"Error: {ex.Message}" };
        }
    }

    public async Task<List<AggregatedIncidentSummary>> GetIncidentsAsync(string role)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<List<AggregatedIncidentSummary>>(
                $"api/reports/incidents?role={role}", _opts);
            return resp ?? new();
        }
        catch { return new(); }
    }

    public async Task<AggregatedIncidentDetail?> GetIncidentDetailAsync(string key, string role)
    {
        try
        {
            return await _http.GetFromJsonAsync<AggregatedIncidentDetail>(
                $"api/reports/incidents/{Uri.EscapeDataString(key)}/details?role={role}", _opts);
        }
        catch { return null; }
    }
}
