using System.Diagnostics;
using System.Text.Json;
using EmergencyPlatform.Models;
using EmergencyPlatform.Services;

namespace EmergencyPlatform.Agents;

/// <summary>
/// Vision Agent – uses GPT-4o vision to analyze emergency scene images.
/// Detects hazards, counts people/vehicles, infers location from signage.
/// </summary>
public class VisionAgent
{
    private readonly AzureOpenAIService _ai;
    private readonly ILogger<VisionAgent> _logger;

    private const string SystemPrompt = """
        You are an emergency dispatch vision analyst.
        Analyze the provided emergency scene image and extract:
        - incident_type  : fire | accident | medical | crime | hazard | unknown
        - severity_level : low | medium | high | critical
        - people_involved: visible count (0 if none)
        - vehicles_involved: visible count (0 if none)
        - hazards        : comma-separated visible hazards (smoke, flames, flooding, downed wires, etc.)
        - location_description: any text/signs/landmarks visible that hint at location
        - visual_cues    : 1-2 sentence description of what you see
        - reasoning_summary: ≤ 2 sentences why you classified it this way

        Respond ONLY with valid JSON (no markdown):
        {
          "incident_type": "",
          "severity_level": "",
          "people_involved": 0,
          "vehicles_involved": 0,
          "hazards": "",
          "location_description": "",
          "visual_cues": "",
          "reasoning_summary": ""
        }
        """;

    public VisionAgent(AzureOpenAIService ai, ILogger<VisionAgent> logger)
    {
        _ai     = ai;
        _logger = logger;
    }

    public async Task<(VisionResult Result, AgentStep Step)> AnalyzeAsync(
        string base64Image, string mimeType = "image/jpeg")
    {
        var sw = Stopwatch.StartNew();

        var raw = await _ai.CompleteWithImageAsync(
            SystemPrompt,
            "Analyze this emergency scene image.",
            base64Image, mimeType, maxTokens: 600);

        sw.Stop();
        _logger.LogDebug("VisionAgent raw: {Raw}", raw);

        VisionResult result;
        try
        {
            var clean = AzureOpenAIService.ExtractJson(raw);
            result = JsonSerializer.Deserialize<VisionResult>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new VisionResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VisionAgent JSON parse failed; using defaults");
            result = new VisionResult { ReasoningSummary = raw };
        }

        return (result, new AgentStep
        {
            Agent      = "VisionAgent",
            Output     = $"type={result.IncidentType}, cues={result.VisualCues}",
            DurationMs = sw.ElapsedMilliseconds
        });
    }
}

public class VisionResult
{
    public string IncidentType        { get; set; } = "unknown";
    public string SeverityLevel       { get; set; } = "low";
    public int    PeopleInvolved      { get; set; }
    public int    VehiclesInvolved    { get; set; }
    public string Hazards             { get; set; } = string.Empty;
    public string LocationDescription { get; set; } = string.Empty;
    public string VisualCues          { get; set; } = string.Empty;
    public string ReasoningSummary    { get; set; } = string.Empty;
}
