using System.Diagnostics;
using System.Text.Json;
using EmergencyPlatform.Models;
using EmergencyPlatform.Services;

namespace EmergencyPlatform.Agents;

public class SeverityAgent
{
    private readonly AzureOpenAIService _ai;
    private readonly ILogger<SeverityAgent> _logger;

    private const string SystemPrompt = """
        You are a senior emergency triage officer.
        Given aggregated signal data, determine final_severity: low | medium | high | critical
        and reasoning in 1-2 sentences.
        Respond ONLY with valid JSON: { "final_severity": "", "reasoning": "" }
        """;

    public SeverityAgent(AzureOpenAIService ai, ILogger<SeverityAgent> logger)
    {
        _ai = ai; _logger = logger;
    }

    public async Task<(string Severity, string Reasoning, AgentStep Step)> ClassifyAsync(
        string aggregatedContext)
    {
        var sw = Stopwatch.StartNew();
        string severity, reasoning;

        try
        {
            var raw = await _ai.CompleteAsync(SystemPrompt, aggregatedContext, maxTokens: 200);
            sw.Stop();
            var clean = AzureOpenAIService.ExtractJson(raw);
            using var doc = JsonDocument.Parse(clean);
            severity  = doc.RootElement.GetProperty("final_severity").GetString() ?? "medium";
            reasoning = doc.RootElement.GetProperty("reasoning").GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeverityAgent AI unavailable — extracting from context");
            sw.Stop();

            // Fallback: extract severity hint from context string
            severity = aggregatedContext.Contains("critical") ? "critical"
                     : aggregatedContext.Contains("high")     ? "high"
                     : aggregatedContext.Contains("medium")   ? "medium"
                     : "low";
            reasoning = $"Severity determined as {severity} based on initial classification.";
        }

        return (severity, reasoning, new AgentStep
        {
            Agent      = "SeverityAgent",
            Output     = $"severity={severity}",
            DurationMs = sw.ElapsedMilliseconds
        });
    }
}