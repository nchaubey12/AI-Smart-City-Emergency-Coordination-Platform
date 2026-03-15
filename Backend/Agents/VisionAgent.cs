using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        // Strip data URI prefix if caller passed it in (e.g. "data:image/jpeg;base64,...")
        if (base64Image.StartsWith("data:"))
        {
            var commaIndex = base64Image.IndexOf(',');
            if (commaIndex >= 0)
            {
                var prefix = base64Image[5..commaIndex];       // "image/jpeg;base64"
                mimeType    = prefix.Split(';')[0];            // "image/jpeg"
                base64Image = base64Image[(commaIndex + 1)..]; // pure base64
                _logger.LogDebug("VisionAgent stripped data URI prefix, resolved mimeType={MimeType}", mimeType);
            }
        }

        // Sanity-check the image before even calling the API
        if (string.IsNullOrWhiteSpace(base64Image))
        {
            sw.Stop();
            _logger.LogError("VisionAgent received null/empty base64Image");
            return (new VisionResult { ReasoningSummary = "No image data provided" },
                    new AgentStep { Agent = "VisionAgent", Output = "error:no-image", DurationMs = sw.ElapsedMilliseconds });
        }

        _logger.LogDebug("VisionAgent image length={Len}, mimeType={MimeType}, preview={Preview}",
            base64Image.Length,
            mimeType,
            base64Image[..Math.Min(20, base64Image.Length)]);

        string raw = string.Empty;

        try
        {
            // HTTP call isolated in its own try so we can distinguish
            // network/auth failures from JSON parse failures
            try
            {
                raw = await _ai.CompleteWithImageAsync(
                    SystemPrompt,
                    "Analyze this emergency scene image.",
                    base64Image, mimeType, maxTokens: 600);
            }
            catch (HttpRequestException httpEx)
            {
                sw.Stop();
                _logger.LogError(httpEx,
                    "VisionAgent HTTP error — check deployment name and that it supports vision. " +
                    "Status={Status} Message={Message}",
                    httpEx.StatusCode, httpEx.Message);

                var code = httpEx.StatusCode.HasValue
                    ? ((int)httpEx.StatusCode.Value).ToString()
                    : "none";

                return (new VisionResult { ReasoningSummary = $"HTTP error {code}: {httpEx.Message}" },
                        new AgentStep { Agent = "VisionAgent", Output = $"http-error:{code}", DurationMs = sw.ElapsedMilliseconds });
            }

            sw.Stop();

            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger.LogWarning("VisionAgent received empty/null response from API");
                return (new VisionResult { ReasoningSummary = "Empty API response" },
                        new AgentStep { Agent = "VisionAgent", Output = "error:empty-response", DurationMs = sw.ElapsedMilliseconds });
            }

            _logger.LogDebug("VisionAgent raw response: {Raw}", raw);

            var clean = AzureOpenAIService.ExtractJson(raw);
            _logger.LogDebug("VisionAgent extracted JSON: {Json}", clean);

            // JsonPropertyName attributes handle snake_case mapping explicitly.
            // SnakeCaseLower policy is a belt-and-suspenders fallback for any
            // fields that don't have an explicit [JsonPropertyName] attribute.
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower
            };

            var result = JsonSerializer.Deserialize<VisionResult>(clean, options)
                ?? new VisionResult();

            _logger.LogInformation(
                "VisionAgent result — type={Type}, severity={Severity}, people={People}, hazards={Hazards}, cues={Cues}",
                result.IncidentType, result.SeverityLevel, result.PeopleInvolved, result.Hazards, result.VisualCues);

            return (result, new AgentStep
            {
                Agent      = "VisionAgent",
                Output     = $"type={result.IncidentType}, severity={result.SeverityLevel}, cues={result.VisualCues}",
                DurationMs = sw.ElapsedMilliseconds
            });
        }
        catch (JsonException jsonEx)
        {
            sw.Stop();
            _logger.LogError(jsonEx,
                "VisionAgent JSON parse failed. Raw response was: {Raw}", raw);

            return (new VisionResult { ReasoningSummary = raw },
                    new AgentStep { Agent = "VisionAgent", Output = "error:parse-failed", DurationMs = sw.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "VisionAgent unexpected error. Raw={Raw}", raw);

            return (new VisionResult { ReasoningSummary = ex.Message },
                    new AgentStep { Agent = "VisionAgent", Output = "error:unexpected", DurationMs = sw.ElapsedMilliseconds });
        }
    }
}

public class VisionResult
{
    [JsonPropertyName("incident_type")]
    public string IncidentType        { get; set; } = "unknown";

    [JsonPropertyName("severity_level")]
    public string SeverityLevel       { get; set; } = "low";

    [JsonPropertyName("people_involved")]
    public int    PeopleInvolved      { get; set; }

    [JsonPropertyName("vehicles_involved")]
    public int    VehiclesInvolved    { get; set; }

    [JsonPropertyName("hazards")]
    public string Hazards             { get; set; } = string.Empty;

    [JsonPropertyName("location_description")]
    public string LocationDescription { get; set; } = string.Empty;

    [JsonPropertyName("visual_cues")]
    public string VisualCues          { get; set; } = string.Empty;

    [JsonPropertyName("reasoning_summary")]
    public string ReasoningSummary    { get; set; } = string.Empty;
}