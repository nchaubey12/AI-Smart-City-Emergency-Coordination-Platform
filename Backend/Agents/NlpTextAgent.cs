using System.Diagnostics;
using System.Text.Json;
using EmergencyPlatform.Models;
using EmergencyPlatform.Services;

namespace EmergencyPlatform.Agents;

/// <summary>
/// NLP Text Agent – classifies emergency type, extracts entities and severity cues
/// from plain text (citizen reports, social-media style messages, etc.).
/// </summary>
public class NlpTextAgent
{
    private readonly AzureOpenAIService _ai;
    private readonly ILogger<NlpTextAgent> _logger;

    private const string SystemPrompt = """
        You are an emergency dispatch NLP agent.
        Given a citizen incident report (text), extract:
        - incident_type  : fire | accident | medical | crime | hazard | unknown
        - severity_level : low | medium | high | critical
        - people_involved: integer count (0 if unknown)
        - vehicles_involved: integer count (0 if unknown)
        - hazards        : short comma-separated list or empty string
        - location_description: best location guess from text, or empty string
        - reasoning_summary: ≤ 2 sentences explaining classification

        Respond ONLY with valid JSON matching this exact schema (no markdown):
        {
          "incident_type": "",
          "severity_level": "",
          "people_involved": 0,
          "vehicles_involved": 0,
          "hazards": "",
          "location_description": "",
          "reasoning_summary": ""
        }
        """;

    public NlpTextAgent(AzureOpenAIService ai, ILogger<NlpTextAgent> logger)
    {
        _ai     = ai;
        _logger = logger;
    }

    public async Task<(NlpResult Result, AgentStep Step)> AnalyzeAsync(string text)
    {
        var sw = Stopwatch.StartNew();

        var raw = await _ai.CompleteAsync(SystemPrompt, text, maxTokens: 500);
        sw.Stop();

        _logger.LogDebug("NlpTextAgent raw: {Raw}", raw);

        NlpResult result;
        try
        {
            var clean = AzureOpenAIService.ExtractJson(raw);
            result = JsonSerializer.Deserialize<NlpResult>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new NlpResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NlpTextAgent JSON parse failed; using defaults");
            result = new NlpResult { ReasoningSummary = raw };
        }

        return (result, new AgentStep
        {
            Agent      = "NlpTextAgent",
            Output     = $"type={result.IncidentType}, severity={result.SeverityLevel}",
            DurationMs = sw.ElapsedMilliseconds
        });
    }
}

public class NlpResult
{
    public string IncidentType        { get; set; } = "unknown";
    public string SeverityLevel       { get; set; } = "low";
    public int    PeopleInvolved      { get; set; }
    public int    VehiclesInvolved    { get; set; }
    public string Hazards             { get; set; } = string.Empty;
    public string LocationDescription { get; set; } = string.Empty;
    public string ReasoningSummary    { get; set; } = string.Empty;
}
