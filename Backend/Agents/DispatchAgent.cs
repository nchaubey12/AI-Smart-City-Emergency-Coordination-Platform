using System.Diagnostics;
using System.Text.Json;
using EmergencyPlatform.Models;
using EmergencyPlatform.Services;

namespace EmergencyPlatform.Agents;

public class DispatchAgent
{
    private readonly AzureOpenAIService _ai;
    private readonly ILogger<DispatchAgent> _logger;

    private const string SystemPrompt = """
        You are an emergency dispatch coordinator. Always assign appropriate units. Never return empty units.

        DISPATCH RULES — follow strictly:
        - incident_type = "fire"     → units MUST include "fire" AND "ambulance"
        - incident_type = "medical"  → units MUST include "ambulance"
        - incident_type = "accident" → units MUST include "police" AND "ambulance"
                                       if flames/fire mentioned → also add "fire"
        - incident_type = "crime"    → units MUST include "police"
                                       if injuries mentioned → also add "ambulance"
        - incident_type = "hazard"   → units MUST include "utility" AND "fire"
        - ANY critical/high severity → always add "police" if not already included

        PRIORITY RULES:
        - severity = "critical" → priority = 1
        - severity = "high"     → priority = 2
        - severity = "medium"   → priority = 3
        - severity = "low"      → priority = 4

        EXAMPLES:
        incident_type=accident, severity=critical, hazards=flames
        → { "units_required": ["police", "ambulance", "fire"], "priority": 1 }

        incident_type=medical, severity=critical
        → { "units_required": ["ambulance", "police"], "priority": 1 }

        incident_type=fire, severity=high
        → { "units_required": ["fire", "ambulance", "police"], "priority": 2 }

        incident_type=crime, severity=medium
        → { "units_required": ["police"], "priority": 3 }

        Respond ONLY with valid JSON, no markdown:
        { "units_required": ["unit1", "unit2"], "priority": 1 }
        """;

    public DispatchAgent(AzureOpenAIService ai, ILogger<DispatchAgent> logger)
    {
        _ai = ai; _logger = logger;
    }

    public async Task<(DispatchRecommendation Recommendation, AgentStep Step)> RecommendAsync(
        string context)
    {
        var sw = Stopwatch.StartNew();
        DispatchRecommendation rec;

        // Always compute rule-based first as safety net
        var ruleRec = FallbackDispatch(context);

        try
        {
            var raw = await _ai.CompleteAsync(SystemPrompt, context, maxTokens: 200);
            sw.Stop();
            var clean = AzureOpenAIService.ExtractJson(raw);
            var aiRec = JsonSerializer.Deserialize<DispatchRecommendation>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (aiRec == null || aiRec.UnitsRequired == null || aiRec.UnitsRequired.Count == 0)
            {
                _logger.LogWarning("AI returned empty dispatch — using rule-based fallback");
                rec = ruleRec;
            }
            else
            {
                // Merge: take AI units but add any critical units the rules say must be there
                var merged = new HashSet<string>(aiRec.UnitsRequired, StringComparer.OrdinalIgnoreCase);
                foreach (var unit in ruleRec.UnitsRequired)
                    merged.Add(unit);

                rec = new DispatchRecommendation
                {
                    UnitsRequired = merged.ToList(),
                    // Use whichever priority is MORE urgent (lower number)
                    Priority = Math.Min(aiRec.Priority, ruleRec.Priority)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DispatchAgent AI unavailable — using rule-based dispatch");
            sw.Stop();
            rec = ruleRec;
        }

        // Final safety net — should never happen but just in case
        if (rec.UnitsRequired == null || rec.UnitsRequired.Count == 0)
            rec = ruleRec;

        return (rec, new AgentStep
        {
            Agent      = "DispatchAgent",
            Output     = $"units=[{string.Join(", ", rec.UnitsRequired)}], priority={rec.Priority}",
            DurationMs = sw.ElapsedMilliseconds
        });
    }

    // ── Rule-based dispatch — always runs as baseline ─────────────────────────

    public static DispatchRecommendation FallbackDispatch(string context)
    {
        var c = context.ToLower();
        var units = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Units by incident type ────────────────────────────────────────────
        if (c.Contains("fire"))
        {
            units.Add("fire");
            units.Add("ambulance");
        }
        if (c.Contains("medical") || ContainsAny(c, "heart attack", "cardiac", "injury",
            "injured", "injuries", "bleeding", "unconscious", "not breathing", "seizure",
            "stroke", "overdose", "hurt", "wounded"))
        {
            units.Add("ambulance");
        }
        if (c.Contains("accident") || ContainsAny(c, "crash", "collision", "highway",
            "vehicle accident", "car crash"))
        {
            units.Add("police");
            units.Add("ambulance");
        }
        if (c.Contains("crime") || ContainsAny(c, "robbery", "shooting", "assault",
            "murder", "theft", "gun", "knife", "attack"))
        {
            units.Add("police");
        }
        if (c.Contains("hazard") || ContainsAny(c, "gas leak", "chemical", "flood",
            "toxic", "spill", "power line"))
        {
            units.Add("utility");
            units.Add("fire");
        }

        // ── Additional units based on context ─────────────────────────────────
        if (ContainsAny(c, "flame", "flaming", "on fire", "burning", "explosion"))
            units.Add("fire");

        if (ContainsAny(c, "critical", "high", "many people", "mass", "50", "100",
                           "urgent", "immediately", "several injured"))
            units.Add("police");

        // Always send at least police
        if (units.Count == 0)
            units.Add("police");

        // ── Priority by severity ──────────────────────────────────────────────
        int priority = c.Contains("critical") ? 1
                     : c.Contains("high")     ? 2
                     : c.Contains("medium")   ? 3
                     : 4;

        return new DispatchRecommendation
        {
            UnitsRequired = units.ToList(),
            Priority      = priority
        };
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k));
}