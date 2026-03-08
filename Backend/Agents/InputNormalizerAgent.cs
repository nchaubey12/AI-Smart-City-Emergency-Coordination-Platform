using EmergencyPlatform.Models;
using EmergencyPlatform.Services;

namespace EmergencyPlatform.Agents;

/// <summary>
/// Input Normalizer Agent – entry point for all report types.
/// Routes to the correct specialist agents and assembles the final IncidentReport.
/// </summary>
public class InputNormalizerAgent
{
    private readonly NlpTextAgent      _nlp;
    private readonly VisionAgent       _vision;
    private readonly SeverityAgent     _severity;
    private readonly DispatchAgent     _dispatch;
    private readonly AzureSpeechService _speech;
    private readonly AzureMapsService  _maps;
    private readonly ILogger<InputNormalizerAgent> _logger;

    public InputNormalizerAgent(
        NlpTextAgent      nlp,
        VisionAgent       vision,
        SeverityAgent     severity,
        DispatchAgent     dispatch,
        AzureSpeechService speech,
        AzureMapsService  maps,
        ILogger<InputNormalizerAgent> logger)
    {
        _nlp      = nlp;
        _vision   = vision;
        _severity = severity;
        _dispatch = dispatch;
        _speech   = speech;
        _maps     = maps;
        _logger   = logger;
    }

    // ── Text report ────────────────────────────────────────────────────────

    public async Task<IncidentReport> ProcessTextAsync(TextReportRequest req)
    {
        _logger.LogInformation("Processing TEXT report");
        var report = new IncidentReport { InputType = "text" };

        // Step 1 – NLP classification
        var (nlp, nlpStep) = await _nlp.AnalyzeAsync(req.Text);
        report.AgentTrace.Add(nlpStep);

        // Step 2 – Severity consolidation
        var sevCtx = $"incident_type={nlp.IncidentType}, severity_hint={nlp.SeverityLevel}, "
                   + $"people={nlp.PeopleInvolved}, hazards={nlp.Hazards}";
        var (sev, sevReason, sevStep) = await _severity.ClassifyAsync(sevCtx);
        report.AgentTrace.Add(sevStep);

        // Step 3 – Dispatch
        var dispCtx = $"incident_type={nlp.IncidentType}, severity={sev}, "
                    + $"people={nlp.PeopleInvolved}, vehicles={nlp.VehiclesInvolved}";
        var (dispatch, dispStep) = await _dispatch.RecommendAsync(dispCtx);
        report.AgentTrace.Add(dispStep);

        // Step 4 – Location resolution
        var location = await ResolveLocationAsync(req.Lat, req.Lon, nlp.LocationDescription);

        // Assemble
        report.IncidentType  = nlp.IncidentType;
        report.SeverityLevel = sev;
        report.LocationData  = location;
        report.Entities      = new EntityData
        {
            PeopleInvolved   = nlp.PeopleInvolved,
            VehiclesInvolved = nlp.VehiclesInvolved,
            Hazards          = nlp.Hazards
        };
        report.ReasoningSummary      = $"{nlp.ReasoningSummary} {sevReason}".Trim();
        report.DispatchRecommendation = dispatch;

        return report;
    }

    // ── Image report ───────────────────────────────────────────────────────

    public async Task<IncidentReport> ProcessImageAsync(ImageReportRequest req)
    {
        _logger.LogInformation("Processing IMAGE report");
        var report = new IncidentReport { InputType = "image" };

        // Step 1 – Vision analysis
        var (vis, visStep) = await _vision.AnalyzeAsync(req.Base64Image, req.MimeType);
        report.AgentTrace.Add(visStep);

        // Step 2 – Severity
        var sevCtx = $"incident_type={vis.IncidentType}, severity_hint={vis.SeverityLevel}, "
                   + $"people={vis.PeopleInvolved}, hazards={vis.Hazards}, "
                   + $"visual_cues={vis.VisualCues}";
        var (sev, sevReason, sevStep) = await _severity.ClassifyAsync(sevCtx);
        report.AgentTrace.Add(sevStep);

        // Step 3 – Dispatch
        var dispCtx = $"incident_type={vis.IncidentType}, severity={sev}, "
                    + $"people={vis.PeopleInvolved}, vehicles={vis.VehiclesInvolved}";
        var (dispatch, dispStep) = await _dispatch.RecommendAsync(dispCtx);
        report.AgentTrace.Add(dispStep);

        // Step 4 – Location
        var location = await ResolveLocationAsync(req.Lat, req.Lon, vis.LocationDescription);

        report.IncidentType  = vis.IncidentType;
        report.SeverityLevel = sev;
        report.LocationData  = location;
        report.Entities      = new EntityData
        {
            PeopleInvolved   = vis.PeopleInvolved,
            VehiclesInvolved = vis.VehiclesInvolved,
            Hazards          = vis.Hazards
        };
        report.ReasoningSummary       = $"{vis.ReasoningSummary} {sevReason}".Trim();
        report.DispatchRecommendation  = dispatch;

        return report;
    }

    // ── Audio report ───────────────────────────────────────────────────────

    public async Task<IncidentReport> ProcessAudioAsync(AudioReportRequest req)
    {
        _logger.LogInformation("Processing AUDIO report");

        // Step 1 – Speech-to-text
        var audioBytes = Convert.FromBase64String(req.Base64Audio);
        var transcript = await _speech.TranscribeAsync(audioBytes, req.MimeType);

        // Step 2 – Feed transcript through text pipeline
        var textReq = new TextReportRequest
        {
            Text = transcript,
            Lat  = req.Lat,
            Lon  = req.Lon
        };
        var report = await ProcessTextAsync(textReq);
        report.InputType = "audio";          // override to reflect true origin
        report.AgentTrace.Insert(0, new AgentStep
        {
            Agent  = "SpeechAgent",
            Output = $"transcript={transcript[..Math.Min(80, transcript.Length)]}…",
            DurationMs = 0
        });

        return report;
    }

    // ── Location resolution helper ─────────────────────────────────────────

    private async Task<LocationData> ResolveLocationAsync(
        double? lat, double? lon, string inferredDescription)
    {
        // If exact GPS provided, use it
        if (lat.HasValue && lon.HasValue)
            return new LocationData
            {
                Lat = lat, Lon = lon,
                InferredLocationDescription = inferredDescription
            };

        // Otherwise geocode the text description
        if (!string.IsNullOrWhiteSpace(inferredDescription))
        {
            try { return await _maps.GeocodeAsync(inferredDescription); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Geocoding failed for '{Desc}'", inferredDescription);
            }
        }

        return new LocationData { InferredLocationDescription = inferredDescription };
    }
}
