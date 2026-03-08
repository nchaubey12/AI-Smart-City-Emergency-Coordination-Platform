using Microsoft.AspNetCore.Mvc;
using EmergencyPlatform.Agents;
using EmergencyPlatform.Models;
using EmergencyPlatform.Services;

namespace EmergencyPlatform.Controllers;

/// <summary>
/// Emergency Report API – accepts text, image, and audio incident reports,
/// runs the multi-agent pipeline, and returns a structured IncidentReport JSON.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EmergencyController : ControllerBase
{
    private readonly InputNormalizerAgent _pipeline;
    private readonly ServiceBusPublisher  _bus;
    private readonly ILogger<EmergencyController> _logger;

    public EmergencyController(
        InputNormalizerAgent pipeline,
        ServiceBusPublisher  bus,
        ILogger<EmergencyController> logger)
    {
        _pipeline = pipeline;
        _bus      = bus;
        _logger   = logger;
    }

    // ── POST /api/emergency/text ──────────────────────────────────────────

    /// <summary>
    /// Submit a plain-text incident report.
    /// </summary>
    [HttpPost("text")]
    [ProducesResponseType(typeof(IncidentReport), 200)]
    public async Task<IActionResult> ReportText([FromBody] TextReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text cannot be empty." });

        _logger.LogInformation("Text report received: {Len} chars", request.Text.Length);
        var report = await _pipeline.ProcessTextAsync(request);
        await _bus.PublishAsync(report);
        return Ok(report);
    }

    // ── POST /api/emergency/image ─────────────────────────────────────────

    /// <summary>
    /// Submit a base64-encoded image (JPEG/PNG) for vision analysis.
    /// </summary>
    [HttpPost("image")]
    [ProducesResponseType(typeof(IncidentReport), 200)]
    public async Task<IActionResult> ReportImage([FromBody] ImageReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Base64Image))
            return BadRequest(new { error = "Base64Image cannot be empty." });

        _logger.LogInformation("Image report received ({Mime})", request.MimeType);
        var report = await _pipeline.ProcessImageAsync(request);
        await _bus.PublishAsync(report);
        return Ok(report);
    }

    // ── POST /api/emergency/image/upload ─────────────────────────────────

    /// <summary>
    /// Submit an image via multipart form upload (convenient for web forms).
    /// </summary>
    [HttpPost("image/upload")]
    [ProducesResponseType(typeof(IncidentReport), 200)]
    public async Task<IActionResult> UploadImage(IFormFile file,
        [FromForm] double? lat, [FromForm] double? lon)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var base64 = Convert.ToBase64String(ms.ToArray());

        var req = new ImageReportRequest
        {
            Base64Image = base64,
            MimeType    = file.ContentType,
            Lat         = lat,
            Lon         = lon
        };

        var report = await _pipeline.ProcessImageAsync(req);
        await _bus.PublishAsync(report);
        return Ok(report);
    }

    // ── POST /api/emergency/audio ─────────────────────────────────────────

    /// <summary>
    /// Submit a base64-encoded audio clip (WAV/MP3) for speech transcription + analysis.
    /// </summary>
    [HttpPost("audio")]
    [ProducesResponseType(typeof(IncidentReport), 200)]
    public async Task<IActionResult> ReportAudio([FromBody] AudioReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Base64Audio))
            return BadRequest(new { error = "Base64Audio cannot be empty." });

        _logger.LogInformation("Audio report received ({Mime})", request.MimeType);
        var report = await _pipeline.ProcessAudioAsync(request);
        await _bus.PublishAsync(report);
        return Ok(report);
    }

    // ── GET /api/emergency/health ─────────────────────────────────────────

    [HttpGet("health")]
    public IActionResult Health() => Ok(new
    {
        status  = "healthy",
        version = "1.0.0",
        time    = DateTime.UtcNow
    });
}
