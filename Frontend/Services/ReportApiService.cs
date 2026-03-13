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
            return await _http.GetFromJsonAsync<List<AggregatedIncidentSummary>>(
                $"api/reports/incidents?role={role}", _opts) ?? new();
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

    public async Task<(bool Success, string Message)> UpdateIncidentAsync(
        string key, string role, UpdateIncidentRequest req)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/reports/incidents/{Uri.EscapeDataString(key)}/update?role={role}", req);
            return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? "Updated." : "Failed.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool Success, string Message)> AcceptIncidentAsync(
        string key, string role, AcceptIncidentRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"api/reports/incidents/{Uri.EscapeDataString(key)}/accept?role={role}", req);
            return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode
                ? "Dispatch triggered successfully." : "Failed to accept.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<(bool Success, string Message)> ResolveIncidentAsync(
        string key, string role, ResolveIncidentRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"api/reports/incidents/{Uri.EscapeDataString(key)}/resolve?role={role}", req);
            return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode
                ? "Incident marked as resolved." : "Failed to resolve.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}