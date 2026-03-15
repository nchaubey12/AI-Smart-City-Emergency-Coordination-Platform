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
        Analyze the provided emergency scene image and extract structured data.

        INCIDENT TYPE — pick exactly one based on what is visually present:
          fire           → visible flames, burning structures, burning vehicles, fire smoke,
                           charred material, embers, firefighters in action
          flood          → standing water on roads or in buildings, flooded streets, people or
                           vehicles partially submerged in water, people wading through water,
                           water flowing through structures, rescue boats on streets
          accident       → vehicle collision, overturned vehicle, crashed car or truck,
                           debris on road from collision, skid marks, damaged vehicles
          medical        → person lying on ground, person receiving CPR, stretcher visible,
                           person clutching chest, visible injury, blood, unconscious person,
                           paramedic treating patient
          crime          → person being restrained, weapons visible, fight in progress,
                           broken glass from robbery, police confronting suspect, crime scene tape
          earthquake     → collapsed building, cracked ground, fallen structures, rubble piles,
                           tilted buildings, destroyed infrastructure, seismic damage
          riot           → large angry crowd, people throwing objects, burning barricades,
                           overturned vehicles set alight, police in riot gear, looting visible
          explosion      → blast crater, destroyed building facade, scorched explosion radius,
                           shattered windows across wide area, explosion aftermath debris
          missing_person → search teams visible, missing person posters, search dogs,
                           people searching an area
          hazard         → ONLY use when none of the above apply. Examples: downed power lines,
                           chemical spill without fire, gas leak visible, radiation warning signs,
                           structural danger without collapse, industrial accident without explosion
          unknown        → image is unclear, unrelated to emergency, or too low quality to classify

        SEVERITY — pick exactly one:
          critical → people visibly trapped, swept away, or unconscious; building collapse in progress;
                     large uncontrolled fire; mass flooding with people in water; explosion aftermath;
                     crowd violence with injuries; 10+ people visibly affected
          high     → confirmed injuries visible, spreading fire, significant flooding,
                     serious vehicle damage with occupants, several people affected,
                     hazardous material visibly leaking near people
          medium   → minor vehicle damage, contained small fire, smoke without visible flames,
                     minor flooding, suspicious activity, possible injuries not confirmed
          low      → no injuries visible, no immediate danger, minor property damage only

        RULES:
          - flood, earthquake, riot, explosion → never low severity
          - people in water or on rooftops → always critical
          - when in doubt between incident types → pick the most specific one visible
          - do not default to "hazard" when a more specific type clearly applies

        Extract:
        - incident_type  : one of the types above
        - severity_level : critical | high | medium | low
        - people_involved: count of visibly affected people (0 if none)
        - vehicles_involved: count of vehicles visible in incident (0 if none)
        - hazards        : comma-separated visible hazards (flames, flooding, smoke, debris,
                           downed wires, chemical spill, structural collapse, etc.)
        - location_description: any text, signs, landmarks, or street names visible
        - visual_cues    : 1-2 sentence factual description of what is seen in the image
        - reasoning_summary: ≤ 2 sentences explaining why this classification was chosen

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
                var prefix  = base64Image[5..commaIndex];       // "image/jpeg;base64"
                mimeType    = prefix.Split(';')[0];             // "image/jpeg"
                base64Image = base64Image[(commaIndex + 1)..];  // pure base64
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
            _logger.LogError(jsonEx, "VisionAgent JSON parse failed. Raw response was: {Raw}", raw);
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