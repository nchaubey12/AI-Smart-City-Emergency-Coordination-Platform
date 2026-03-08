using System.Diagnostics;
using System.Text.Json;
using EmergencyPlatform.Models;
using EmergencyPlatform.Services;

namespace EmergencyPlatform.Agents;

/// <summary>
/// Dispatch Reasoning Agent – decides which emergency units to send and
/// assigns a dispatch priority (1 = highest, 5 = lowest).
/// </summary>
public class DispatchAgent
{
    private readonly AzureOpenAIService _ai;
    private readonly ILogger<DispatchAgent> _logger;

    private const string SystemPrompt = """
        You are an emergency dispatch coordinator.
        Based on incident classification, severity, and entity data, determine:
        - units_required : array from [ "police", "fire", "ambulance", "rescue", "utility" ]
        - priority       : integer 1–5  (1=immediate, 5=low-priority)

        Dispatch rules:
          critical  → priority 1, include ambulance + relevant units
          high      → priority 2
          medium    → priority 3
          low       → priority 4 or 5
          fire incidents  → always include "fire"
          medical         → always include "ambulance"
          crime           → always include "police"
          hazard/utility  → include "utility" and possibly "fire"
          accident        → "police" + "ambulance" at minimum

        Respond ONLY with valid JSON (no markdown):
        { "units_required": [], "priority": 3 }
        """;

    public DispatchAgent(AzureOpenAIService ai, ILogger<DispatchAgent> logger)
    {
        _ai     = ai;
        _logger = logger;
    }

    public async Task<(DispatchRecommendation Recommendation, AgentStep Step)> RecommendAsync(
        string context)
    {
        var sw = Stopwatch.StartNew();
        var raw = await _ai.CompleteAsync(SystemPrompt, context, maxTokens: 200);
        sw.Stop();

        DispatchRecommendation rec;
        try
        {
            var clean = AzureOpenAIService.ExtractJson(raw);
            rec = JsonSerializer.Deserialize<DispatchRecommendation>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new DispatchRecommendation();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DispatchAgent JSON parse failed; using fallback");
            rec = new DispatchRecommendation
            {
                UnitsRequired = new List<string> { "police", "ambulance" },
                Priority      = 3
            };
        }

        return (rec, new AgentStep
        {
            Agent      = "DispatchAgent",
            Output     = $"units=[{string.Join(",", rec.UnitsRequired)}], priority={rec.Priority}",
            DurationMs = sw.ElapsedMilliseconds
        });
    }
}
