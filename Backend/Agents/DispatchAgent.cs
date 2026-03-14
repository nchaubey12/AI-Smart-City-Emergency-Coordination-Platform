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
        You are an emergency dispatch coordinator.

        AVAILABLE UNITS: police, traffic_police, ambulance, fire, tow_truck, utility,
                         rescue, coast_guard, bomb_squad, hazmat, national_guard, riot_control

        DISPATCH RULES:
        fire         → fire, ambulance; if on road: also traffic_police, tow_truck
        explosion    → fire, ambulance, police, bomb_squad, hazmat
        accident     → police, traffic_police, ambulance, tow_truck; if flames: also fire
        medical      → ambulance; if critical/many: also police
        crime        → police; if injuries: also ambulance
        flood        → rescue, utility, ambulance, police; if large scale: coast_guard
        earthquake   → rescue, ambulance, fire, police, utility
        riot         → police, riot_control, ambulance; if critical: national_guard
        hazard       → utility, fire; if flooding: also rescue; if chemical: also hazmat
        missing_person → police

        PRIORITY: critical=1, high=2, medium=3, low=4

        Respond ONLY with valid JSON, no markdown:
        { "units_required": ["unit1", "unit2"], "priority": 1 }
        """;

    public DispatchAgent(AzureOpenAIService ai, ILogger<DispatchAgent> logger)
    {
        _ai = ai; _logger = logger;
    }

    public async Task<(DispatchRecommendation Recommendation, AgentStep Step)> RecommendAsync(string context)
    {
        var sw = Stopwatch.StartNew();
        var ruleRec = FallbackDispatch(context);
        DispatchRecommendation rec;

        try
        {
            var raw = await _ai.CompleteAsync(SystemPrompt, context, maxTokens: 200);
            sw.Stop();
            var clean = AzureOpenAIService.ExtractJson(raw);
            var aiRec = JsonSerializer.Deserialize<DispatchRecommendation>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (aiRec == null || aiRec.UnitsRequired == null || aiRec.UnitsRequired.Count == 0)
            {
                rec = ruleRec;
            }
            else
            {
                var merged = new HashSet<string>(aiRec.UnitsRequired, StringComparer.OrdinalIgnoreCase);
                foreach (var u in ruleRec.UnitsRequired) merged.Add(u);
                rec = new DispatchRecommendation
                {
                    UnitsRequired = merged.ToList(),
                    Priority = Math.Min(aiRec.Priority > 0 ? aiRec.Priority : 4,
                                        ruleRec.Priority > 0 ? ruleRec.Priority : 4)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DispatchAgent AI unavailable — using rule-based");
            sw.Stop();
            rec = ruleRec;
        }

        if (rec.UnitsRequired == null || rec.UnitsRequired.Count == 0) rec = ruleRec;

        return (rec, new AgentStep
        {
            Agent      = "DispatchAgent",
            Output     = $"units=[{string.Join(", ", rec.UnitsRequired)}], priority={rec.Priority}",
            DurationMs = sw.ElapsedMilliseconds
        });
    }

    public static DispatchRecommendation FallbackDispatch(string context)
    {
        var c = context.ToLower();
        var units = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool isAccident  = ContainsAny(c, "accident", "crash", "collision", "car crash", "pileup");
        bool isFire      = ContainsAny(c, "fire", "flame", "burning", "blaze", "arson");
        bool isExplosion = ContainsAny(c, "explosion", "explode", "blast", "bomb", "detonation");
        bool isMedical   = ContainsAny(c, "medical", "heart attack", "cardiac", "injured",
                                          "injuries", "unconscious", "bleeding", "not breathing",
                                          "seizure", "stroke", "hurt", "wounded");
        bool isCrime     = ContainsAny(c, "crime", "robbery", "shooting", "assault", "murder",
                                          "gun", "knife", "attack", "theft", "armed");
        bool isFlood     = ContainsAny(c, "flood", "flooding", "overflowed", "flash flood",
                                          "submerged", "water level", "river burst");
        bool isEarthquake = ContainsAny(c, "earthquake", "tremor", "seismic", "quake", "ground shaking");
        bool isRiot      = ContainsAny(c, "riot", "looting", "civil unrest", "mob", "crowd violence");
        bool isHazard    = ContainsAny(c, "gas leak", "chemical", "toxic", "power line", "hazardous");
        bool isMissing   = ContainsAny(c, "missing", "lost child", "disappeared");

        bool onRoad      = ContainsAny(c, "highway", "motorway", "traffic", "junction", "lane");
        bool hasFlames   = ContainsAny(c, "flame", "flaming", "on fire", "in flames", "burning");
        bool hasInjured  = ContainsAny(c, "injur", "hurt", "wounded", "bleeding", "unconscious");
        bool isCritical  = ContainsAny(c, "critical", "many people", "urgent", "trapped", "mass",
                                          "dozens", "50", "100");

        if (isAccident)
        {
            units.Add("police"); units.Add("ambulance"); units.Add("tow_truck");
            if (onRoad) units.Add("traffic_police");
            if (hasFlames) units.Add("fire");
        }
        if (isFire)
        {
            units.Add("fire"); units.Add("ambulance");
            if (onRoad) { units.Add("traffic_police"); units.Add("tow_truck"); }
            if (isCritical) units.Add("police");
        }
        if (isExplosion)
        {
            units.Add("fire"); units.Add("ambulance");
            units.Add("police"); units.Add("bomb_squad"); units.Add("hazmat");
        }
        if (isMedical)
        {
            units.Add("ambulance");
            if (isCritical) units.Add("police");
        }
        if (isCrime)
        {
            units.Add("police");
            if (hasInjured) units.Add("ambulance");
        }
        if (isFlood)
        {
            units.Add("rescue"); units.Add("utility");
            units.Add("ambulance"); units.Add("police");
            if (isCritical) units.Add("coast_guard");
        }
        if (isEarthquake)
        {
            units.Add("rescue"); units.Add("ambulance");
            units.Add("fire"); units.Add("police"); units.Add("utility");
        }
        if (isRiot)
        {
            units.Add("police"); units.Add("riot_control"); units.Add("ambulance");
            if (isCritical) units.Add("national_guard");
        }
        if (isHazard)
        {
            units.Add("utility"); units.Add("fire");
            if (c.Contains("chemical") || c.Contains("toxic")) units.Add("hazmat");
        }
        if (isMissing) units.Add("police");

        if (ContainsAny(c, "traffic jam", "blocked", "congestion")) units.Add("traffic_police");
        if (units.Count == 0) units.Add("police");

        int priority = isCritical ? 1
            : ContainsAny(c, "high", "injured", "serious", "severe", "flood", "earthquake", "riot") ? 2
            : ContainsAny(c, "medium", "possible", "minor") ? 3
            : 4;

        return new DispatchRecommendation { UnitsRequired = units.ToList(), Priority = priority };
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k));
}