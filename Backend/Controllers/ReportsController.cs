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
            // Run AI pipeline based on input type
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

                default: // text
                    analysis = await _pipeline.ProcessTextAsync(new TextReportRequest
                    {
                        Text = req.Description,
                        Lat  = req.Lat,
                        Lon  = req.Lon
                    });
                    break;
            }

            // Build stored report
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
                Description = req.Description,
                Location    = location,
                Lat         = req.Lat ?? analysis.LocationData.Lat,
                Lon         = req.Lon ?? analysis.LocationData.Lon,
                SubmittedAt = DateTime.UtcNow
            };

            var reportId = await _storage.SaveReportAsync(stored, analysis);
            await _bus.PublishAsync(analysis);

            // Role-based response: Users don't see analysis
            if (req.UserRole == "Admin")
            {
                return Ok(new SubmitReportResponse
                {
                    Success  = true,
                    Message  = "Report submitted and analyzed.",
                    ReportId = reportId,
                    Analysis = analysis
                });
            }
            else
            {
                return Ok(new SubmitReportResponse
                {
                    Success  = true,
                    Message  = "Thank you for your information. We will forward this message to the appropriate authority.",
                    ReportId = reportId,
                    Analysis = null // hidden from regular users
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report submission failed");
            return StatusCode(500, new { error = "Analysis failed. Please try again." });
        }
    }

    // ── GET /api/reports/incidents — Admin only ───────────────────────────────

    [HttpGet("incidents")]
    public async Task<IActionResult> GetIncidents([FromQuery] string role = "")
    {
        if (role != "Admin")
            return Forbid();

        var store = await _storage.LoadReportsAsync();
        // Return without full report details for list view
        var summary = store.Incidents.Select(i => new
        {
            i.IncidentKey,
            i.IncidentType,
            i.SeverityLevel,
            i.Location,
            i.Lat,
            i.Lon,
            i.FirstReportedAt,
            i.LastReportedAt,
            i.ReportCount,
            i.DispatchUnits,
            i.Priority
        }).OrderByDescending(i => i.LastReportedAt);

        return Ok(summary);
    }

    // ── GET /api/reports/incidents/{key}/details — Admin only ─────────────────

    [HttpGet("incidents/{key}/details")]
    public async Task<IActionResult> GetIncidentDetails(string key, [FromQuery] string role = "")
    {
        if (role != "Admin")
            return Forbid();

        var store    = await _storage.LoadReportsAsync();
        var incident = store.Incidents.FirstOrDefault(i => i.IncidentKey == key);
        if (incident == null)
            return NotFound(new { error = "Incident not found." });

        return Ok(incident);
    }
}
