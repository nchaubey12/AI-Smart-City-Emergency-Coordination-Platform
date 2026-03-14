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
        catch (Exception ex) { return new SubmitReportResponse { Success = false, Message = ex.Message }; }
    }

    public async Task<List<AggregatedIncidentSummary>> GetIncidentsAsync(string role)
    {
        try { return await _http.GetFromJsonAsync<List<AggregatedIncidentSummary>>(
            $"api/reports/incidents?role={role}", _opts) ?? new(); }
        catch { return new(); }
    }

    public async Task<AggregatedIncidentDetail?> GetIncidentDetailAsync(string key, string role)
    {
        try { return await _http.GetFromJsonAsync<AggregatedIncidentDetail>(
            $"api/reports/incidents/{Uri.EscapeDataString(key)}/details?role={role}", _opts); }
        catch { return null; }
    }

    public async Task<List<OpenIncidentOption>> GetOpenIncidentsAsync(string role)
    {
        try { return await _http.GetFromJsonAsync<List<OpenIncidentOption>>(
            $"api/reports/incidents/open?role={role}", _opts) ?? new(); }
        catch { return new(); }
    }

    public async Task<(bool, string)> UpdateIncidentAsync(string key, string role, UpdateIncidentRequest req)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/reports/incidents/{Uri.EscapeDataString(key)}/update?role={role}", req);
            return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? "Saved." : "Failed.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool, string)> AcceptIncidentAsync(string key, string role, AcceptIncidentRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"api/reports/incidents/{Uri.EscapeDataString(key)}/accept?role={role}", req);
            return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? "Dispatch confirmed." : "Failed.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool, string)> ResolveIncidentAsync(string key, string role, ResolveIncidentRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"api/reports/incidents/{Uri.EscapeDataString(key)}/resolve?role={role}", req);
            return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? "Resolved." : "Failed.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool, string)> MarkDuplicateAsync(string role, MarkDuplicateRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"api/reports/mark-duplicate?role={role}", req);
            return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? "Report moved." : "Failed.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}