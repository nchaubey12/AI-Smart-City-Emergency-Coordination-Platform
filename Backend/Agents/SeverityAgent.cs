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
        You are a senior emergency triage officer. Given aggregated signal data from an
        incident report, determine the final_severity and provide brief reasoning.

        SEVERITY RULES — apply in order, escalate to the highest matching level:

        CRITICAL — any of the following:
          - incident_type is flood, earthquake, riot, or explosion
          - people trapped, families trapped, houses flooded, swept away, on rooftops
          - cardiac arrest, not breathing, unresponsive, mass casualty, active shooter
          - 10 or more people involved, crowd violence, hostage situation
          - dam break, flash flood, building collapse, wildfire spreading
          - bomb, IED, detonated, armed gunman, hostage
          - words: urgent, immediately, critical, life threatening, dying, rescue needed,
                   engulfed, out of control, catastrophic, major disaster

        HIGH — any of the following:
          - confirmed injuries, unconscious person, serious accident
          - spreading fire, significant flooding, river overflowing, river burst
          - several people injured, multiple vehicles in collision
          - armed crime, large fire, gas leak near people, chemical exposure
          - structural collapse, bridge damage, large-scale evacuation
          - words: serious, severe, significant, spreading, multiple injured,
                   unconscious, armed, overflowed, submerged, evacuating

        MEDIUM — any of the following:
          - possible injuries, contained incident, minor accident, fender bender
          - small fire, minor flooding, smoke reported without confirmed fire
          - suspicious activity, non-life-threatening injury, verbal altercation
          - words: possible, minor, contained, smoke, suspicious, reported,
                   fender bender, non-urgent, potential

        LOW — ONLY when all of the following are true:
          - no injuries confirmed or suspected
          - no immediate danger to life or property
          - incident is fully contained or non-emergency in nature
          - words: no injuries, minor issue, noise complaint, stray animal,
                   non-urgent, property damage only, already handled

        ABSOLUTE RULES:
          - flood, earthquake, riot, explosion → NEVER low, almost always critical
          - trapped people or swept away → always critical
          - when in doubt between two levels → choose the higher one
          - do not downgrade based on low people count alone if the incident type is severe

        Respond ONLY with valid JSON, no markdown:
        { "final_severity": "", "reasoning": "" }
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
            _logger.LogWarning(ex, "SeverityAgent AI unavailable — using rule-based fallback");
            sw.Stop();
            (severity, reasoning) = FallbackClassify(aggregatedContext);
        }

        return (severity, reasoning, new AgentStep
        {
            Agent      = "SeverityAgent",
            Output     = $"severity={severity}",
            DurationMs = sw.ElapsedMilliseconds
        });
    }

    // Mirrors the prompt rules exactly so fallback behaves identically to the AI path.
    private static (string Severity, string Reasoning) FallbackClassify(string ctx)
    {
        var t = ctx.ToLower();

        // Critical — incident type check first (most reliable signal)
        var criticalTypes = new[]
        {
            "incident_type=flood", "incident_type=earthquake",
            "incident_type=riot",  "incident_type=explosion"
        };

        bool isCritical =
            criticalTypes.Any(t.Contains) ||
            ContainsAny(t,
                "trapped", "families trapped", "houses flooded", "swept away", "rooftop",
                "cardiac arrest", "not breathing", "unresponsive", "mass casualty",
                "active shooter", "hostage", "dam break", "flash flood",
                "building collapse", "building collapsed", "wildfire spreading",
                "bomb", "ied", "detonated", "armed gunman",
                "urgent", "immediately", "life threatening", "dying",
                "rescue needed", "critical", "engulfed", "out of control",
                "catastrophic", "major disaster", "crowd violence");

        bool isHigh = !isCritical && ContainsAny(t,
            "injured", "injury", "injuries", "unconscious", "serious", "severe",
            "spreading", "significant flooding", "river burst", "river overflow",
            "several", "multiple", "bleeding", "crash", "collision", "hurt",
            "submerged", "overflowed", "evacuating", "armed",
            "structural collapse", "bridge damage", "chemical exposure",
            "gas leak near", "large fire", "spreading fire", "multiple vehicles",
            "vehicle rollover", "bus accident", "significant");

        bool isMedium = !isCritical && !isHigh && ContainsAny(t,
            "possible", "minor", "contained", "smoke", "small fire",
            "accident", "help", "emergency", "gas", "damage",
            "suspicious", "fender bender", "verbal", "reported",
            "non-urgent", "potential", "minor flooding");

        var severity = isCritical ? "critical"
                     : isHigh    ? "high"
                     : isMedium  ? "medium"
                     : "low";

        return (severity, $"Rule-based fallback determined severity as {severity}.");
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(text.Contains);
}