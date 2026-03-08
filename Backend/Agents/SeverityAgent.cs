using System.Diagnostics;
using System.Text.Json;
using EmergencyPlatform.Models;
using EmergencyPlatform.Services;

namespace EmergencyPlatform.Agents;

/// <summary>
/// Severity Classification Agent – synthesizes NLP + Vision signals (or a single
/// source) to produce the final severity rating with reasoning.
/// </summary>
public class SeverityAgent
{
    private readonly AzureOpenAIService _ai;
    private readonly ILogger<SeverityAgent> _logger;

    private const string SystemPrompt = """
        You are a senior emergency triage officer.
        Given aggregated signal data from NLP and/or Vision agents, determine:
        - final_severity : low | medium | high | critical
        - reasoning      : ≤ 2 sentences explaining the final rating

        Severity rules (apply strictly):
          critical  – imminent threat to life, active fire/explosion, mass casualty
          high      – injuries confirmed or highly likely, significant hazard spreading
          medium    – contained hazard, property damage, possible injury
          low       – minor incident, no confirmed injuries, low public risk

        Respond ONLY with valid JSON (no markdown):
        { "final_severity": "", "reasoning": "" }
        """;

    public SeverityAgent(AzureOpenAIService ai, ILogger<SeverityAgent> logger)
    {
        _ai     = ai;
        _logger = logger;
    }

    public async Task<(string Severity, string Reasoning, AgentStep Step)> ClassifyAsync(
        string aggregatedContext)
    {
        var sw = Stopwatch.StartNew();
        var raw = await _ai.CompleteAsync(SystemPrompt, aggregatedContext, maxTokens: 200);
        sw.Stop();

        string severity = "medium", reasoning = raw;
        try
        {
            var clean = AzureOpenAIService.ExtractJson(raw);
            using var doc = JsonDocument.Parse(clean);
            severity  = doc.RootElement.GetProperty("final_severity").GetString() ?? "medium";
            reasoning = doc.RootElement.GetProperty("reasoning").GetString() ?? raw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeverityAgent JSON parse failed");
        }

        return (severity, reasoning, new AgentStep
        {
            Agent      = "SeverityAgent",
            Output     = $"severity={severity}",
            DurationMs = sw.ElapsedMilliseconds
        });
    }
}
