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
        _pipeline = pipeline;
        _storage  = storage;
        _bus      = bus;
        _logger   = logger;
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
                    {
                        Base64Image = req.Base64Image,
                        MimeType    = req.MimeType,
                        Lat = req.Lat, Lon = req.Lon
                    });
                    break;
                case "audio":
                    analysis = await _pipeline.ProcessAudioAsync(new AudioReportRequest
                    {
                        Base64Audio = req.Base64Audio,
                        MimeType    = req.MimeType,
                        Lat = req.Lat, Lon = req.Lon
                    });
                    break;
                default:
                    analysis = await _pipeline.ProcessTextAsync(new TextReportRequest
                    {
                        Text = req.Description,
                        Lat  = req.Lat,
                        Lon  = req.Lon
                    });
                    break;
            }

            var location = req.Lat.HasValue
                ? $"{req.Lat:F4}, {req.Lon:F4}"
                : analysis.LocationData.InferredLocationDescription;

            var description = !string.IsNullOrWhiteSpace(req.Description)
                ? req.Description
                : analysis.ReasoningSummary;

            var stored = new StoredReport
            {
                UserId      = req.UserId,
                UserName    = req.UserName,
                UserEmail   = req.UserEmail,
                UserRole    = req.UserRole,
                InputType   = req.InputType,
                Description = description,
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

            if (req.UserRole == "Admin")
                return Ok(new SubmitReportResponse
                {
                    Success = true, Message = "Report submitted and analyzed.",
                    ReportId = reportId, Analysis = analysis
                });
            else
                return Ok(new SubmitReportResponse
                {
                    Success = true,
                    Message = "Thank you for your information. We will forward this message to the appropriate authority.",
                    ReportId = reportId, Analysis = null
                });
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
            i.AdminDispatchUnits,
            EffectiveDispatchUnits = i.EffectiveDispatchUnits,
            i.Priority,
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
        if (incident == null) return NotFound(new { error = "Incident not found." });

        return Ok(incident);
    }

    // ── PATCH /api/reports/incidents/{key}/update — Admin override ────────────

    [HttpPatch("incidents/{key}/update")]
    public async Task<IActionResult> UpdateIncident(string key, [FromQuery] string role,
        [FromBody] UpdateIncidentRequest req)
    {
        if (role != "Admin") return Forbid();

        var result = await _storage.UpdateIncidentAsync(key, req);
        if (!result) return NotFound(new { error = "Incident not found." });

        return Ok(new { success = true, message = "Incident updated." });
    }

    // ── POST /api/reports/incidents/{key}/accept — Trigger dispatch ───────────

    [HttpPost("incidents/{key}/accept")]
    public async Task<IActionResult> AcceptIncident(string key, [FromQuery] string role,
        [FromBody] AcceptIncidentRequest req)
    {
        if (role != "Admin") return Forbid();

        var result = await _storage.AcceptIncidentAsync(key, req);
        if (!result) return NotFound(new { error = "Incident not found." });

        _logger.LogInformation("Incident {Key} accepted by {Admin} — dispatch triggered", key, req.AdminName);
        return Ok(new { success = true, message = "Incident accepted. Dispatch teams notified." });
    }

    // ── POST /api/reports/incidents/{key}/resolve — Mark resolved ─────────────

    [HttpPost("incidents/{key}/resolve")]
    public async Task<IActionResult> ResolveIncident(string key, [FromQuery] string role,
        [FromBody] ResolveIncidentRequest req)
    {
        if (role != "Admin") return Forbid();

        var result = await _storage.ResolveIncidentAsync(key, req);
        if (!result) return NotFound(new { error = "Incident not found." });

        _logger.LogInformation("Incident {Key} resolved by {Admin}", key, req.AdminName);
        return Ok(new { success = true, message = "Incident marked as resolved." });
    }
}