using Microsoft.AspNetCore.Mvc;
using EmergencyPlatform.Agents;
using EmergencyPlatform.Models;
using EmergencyPlatform.Services;

namespace EmergencyPlatform.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly InputNormalizerAgent _pipeline;
    private readonly JsonStorageService   _storage;
    private readonly ServiceBusPublisher  _bus;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(InputNormalizerAgent pipeline, JsonStorageService storage,
        ServiceBusPublisher bus, ILogger<ReportsController> logger)
    {
        _pipeline = pipeline; _storage = storage; _bus = bus; _logger = logger;
    }

    // ── POST /api/reports/submit ──────────────────────────────────────────────

    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] SubmitReportRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.UserId))
            return Unauthorized(new { error = "Not authenticated." });
        try
        {
            IncidentReport analysis;
            switch (req.InputType.ToLower())
            {
                case "image":
                    analysis = await _pipeline.ProcessImageAsync(new ImageReportRequest
                        { Base64Image = req.Base64Image, MimeType = req.MimeType, Lat = req.Lat, Lon = req.Lon });
                    break;
                case "audio":
                    analysis = await _pipeline.ProcessAudioAsync(new AudioReportRequest
                        { Base64Audio = req.Base64Audio, MimeType = req.MimeType, Lat = req.Lat, Lon = req.Lon });
                    break;
                default:
                    analysis = await _pipeline.ProcessTextAsync(new TextReportRequest
                        { Text = req.Description, Lat = req.Lat, Lon = req.Lon });
                    break;
            }

            var location = req.Lat.HasValue
                ? $"{req.Lat:F4}, {req.Lon:F4}"
                : analysis.LocationData.InferredLocationDescription;

            var stored = new StoredReport
            {
                UserId      = req.UserId,
                UserName    = req.UserName,
                UserEmail   = req.UserEmail,
                UserRole    = req.UserRole,
                InputType   = req.InputType,
                Description = !string.IsNullOrWhiteSpace(req.Description)
                                ? req.Description : analysis.ReasoningSummary,
                Location    = location,
                Lat         = req.Lat ?? analysis.LocationData.Lat,
                Lon         = req.Lon ?? analysis.LocationData.Lon,
                SubmittedAt = DateTime.UtcNow,
                UploadedImage = req.Base64Image,
                UploadedMimeType = req.MimeType,
                UploadedAudio = req.Base64Audio
            };

            var reportId = await _storage.SaveReportAsync(stored, analysis);
            await _bus.PublishAsync(analysis);

            return req.UserRole == "Admin"
                ? Ok(new SubmitReportResponse { Success = true, Message = "Report submitted and analyzed.",
                    ReportId = reportId, Analysis = analysis })
                : Ok(new SubmitReportResponse { Success = true,
                    Message = "Thank you for your information. We will forward this message to the appropriate authority.",
                    ReportId = reportId, Analysis = null });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report submission failed");
            return StatusCode(500, new { error = "Analysis failed. Please try again." });
        }
    }

    // ── GET /api/reports/incidents ────────────────────────────────────────────

    [HttpGet("incidents")]
    public async Task<IActionResult> GetIncidents([FromQuery] string role = "")
    {
        if (role != "Admin") return Forbid();
        var store = await _storage.LoadReportsAsync();
        var summary = store.Incidents.Select(i => new
        {
            i.IncidentKey,
            i.IncidentType,
            i.AdminIncidentType,
            EffectiveIncidentType = i.EffectiveIncidentType,
            i.SeverityLevel,
            i.AdminSeverity,
            EffectiveSeverity = i.EffectiveSeverity,
            i.Location,
            i.Lat,
            i.Lon,
            i.FirstReportedAt,
            i.LastReportedAt,
            i.ReportCount,
            i.DispatchUnits,
            i.Status,
            i.AcceptedAt,
            i.AcceptedBy,
            i.ResolvedAt,
            i.ResolvedBy,
            i.AdminNotes,
            i.DispatchTriggeredAt,
            Description     = i.Reports.FirstOrDefault()?.Description ?? string.Empty,
            AnalysisSummary = i.Reports.FirstOrDefault()?.AnalysisResult?.ReasoningSummary ?? string.Empty
        }).OrderByDescending(i => i.LastReportedAt);

        return Ok(summary);
    }

    // ── GET /api/reports/incidents/{key}/details ──────────────────────────────

    [HttpGet("incidents/{key}/details")]
    public async Task<IActionResult> GetIncidentDetails(string key, [FromQuery] string role = "")
    {
        if (role != "Admin") return Forbid();
        var store    = await _storage.LoadReportsAsync();
        var incident = store.Incidents.FirstOrDefault(i => i.IncidentKey == key);
        return incident == null ? NotFound(new { error = "Not found." }) : Ok(incident);
    }

    // ── GET /api/reports/incidents/open — for duplicate dropdown ─────────────
    // Only accepted + resolved are returned. Open incidents are excluded because
    // new same-location reports already auto-cluster into the open row.

    [HttpGet("incidents/open")]
    public async Task<IActionResult> GetClosedForDuplicate([FromQuery] string role = "")
    {
        if (role != "Admin") return Forbid();
        var store = await _storage.LoadReportsAsync();
        var result = store.Incidents
            .Where(i => i.Status == "accepted" || i.Status == "resolved")
            .Select(i => new { i.IncidentKey, i.EffectiveIncidentType, i.Location, i.Status })
            .OrderByDescending(i => i.Status == "accepted"); // dispatched first, then resolved
        return Ok(result);
    }

    // ── PATCH /api/reports/incidents/{key}/update ─────────────────────────────

    [HttpPatch("incidents/{key}/update")]
    public async Task<IActionResult> UpdateIncident(string key, [FromQuery] string role,
        [FromBody] UpdateIncidentRequest req)
    {
        if (role != "Admin") return Forbid();
        return await _storage.UpdateIncidentAsync(key, req)
            ? Ok(new { success = true })
            : NotFound(new { error = "Not found." });
    }

    // ── POST /api/reports/incidents/{key}/accept ──────────────────────────────

    [HttpPost("incidents/{key}/accept")]
    public async Task<IActionResult> AcceptIncident(string key, [FromQuery] string role,
        [FromBody] AcceptIncidentRequest req)
    {
        if (role != "Admin") return Forbid();
        return await _storage.AcceptIncidentAsync(key, req)
            ? Ok(new { success = true, message = "Dispatch confirmed." })
            : NotFound(new { error = "Not found." });
    }

    // ── POST /api/reports/incidents/{key}/resolve ─────────────────────────────

    [HttpPost("incidents/{key}/resolve")]
    public async Task<IActionResult> ResolveIncident(string key, [FromQuery] string role,
        [FromBody] ResolveIncidentRequest req)
    {
        if (role != "Admin") return Forbid();
        return await _storage.ResolveIncidentAsync(key, req)
            ? Ok(new { success = true, message = "Incident resolved." })
            : NotFound(new { error = "Not found." });
    }

    // ── POST /api/reports/mark-duplicate ─────────────────────────────────────

    [HttpPost("mark-duplicate")]
    public async Task<IActionResult> MarkDuplicate([FromQuery] string role,
        [FromBody] MarkDuplicateRequest req)
    {
        if (role != "Admin") return Forbid();
        return await _storage.MarkDuplicateAsync(req)
            ? Ok(new { success = true, message = "Report moved to target incident." })
            : NotFound(new { error = "Report or target incident not found." });
    }
}