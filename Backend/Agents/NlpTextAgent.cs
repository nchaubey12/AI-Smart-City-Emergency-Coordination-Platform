using System.Diagnostics;
using System.Text.Json;
using EmergencyPlatform.Models;
using EmergencyPlatform.Services;

namespace EmergencyPlatform.Agents;

public class NlpTextAgent
{
    private readonly AzureOpenAIService _ai;
    private readonly ILogger<NlpTextAgent> _logger;

    private const string SystemPrompt = """
        You are an emergency dispatch classifier. Be strict and decisive.

        INCIDENT TYPE — pick exactly one:
        - "fire"         → fire, flames, burning, blaze, arson
        - "explosion"    → explosion, blast, bomb, detonation
        - "accident"     → car crash, vehicle collision, highway accident, road accident, pileup
        - "medical"      → heart attack, cardiac arrest, injury, bleeding, unconscious, seizure,
                           hurt people, injured people, not breathing, fainted, stroke, overdose
        - "crime"        → robbery, shooting, assault, theft, murder, stabbing, violence, gun, knife
        - "flood"        → flooding, flood, overflowed river, flash flood, water level rising, submerged
        - "earthquake"   → earthquake, tremor, seismic, ground shaking, building collapse due to quake
        - "riot"         → riot, civil unrest, mob, looting, protest turned violent, crowd violence
        - "hazard"       → gas leak, chemical spill, downed power line, toxic, hazardous material
                           (use this ONLY when no more specific type fits)
        - "missing_person" → missing person, lost child, person not found
        - "unknown"      → ONLY if absolutely none of the above apply

        SEVERITY — pick exactly one:
        - "critical" → 10+ people injured OR cardiac arrest OR not breathing OR explosion OR
                       people trapped OR urgent OR mass casualty OR families trapped OR houses flooded
                       OR earthquake OR riot with violence
        - "high"     → confirmed injuries, serious accident, unconscious person, spreading fire,
                       several injured, significant flooding
        - "medium"   → possible injuries, contained incident, minor accident, small fire
        - "low"      → no injuries, no immediate danger, minor issue

        IMPORTANT: flood, earthquake, riot are almost always critical or high — never low.

        Respond ONLY with this exact JSON, no markdown:
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
        _ai = ai; _logger = logger;
    }

    public async Task<(NlpResult Result, AgentStep Step)> AnalyzeAsync(string text)
    {
        var sw = Stopwatch.StartNew();
        NlpResult result;
        var ruleResult = FallbackClassify(text);

        try
        {
            var raw = await _ai.CompleteAsync(SystemPrompt, text, maxTokens: 500);
            sw.Stop();
            var clean = AzureOpenAIService.ExtractJson(raw);
            var aiResult = JsonSerializer.Deserialize<NlpResult>(clean,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (aiResult == null || aiResult.IncidentType == "unknown"
                || string.IsNullOrEmpty(aiResult.IncidentType))
            {
                _logger.LogWarning("AI returned unknown — using rule-based fallback");
                result = ruleResult;
            }
            else
            {
                result = aiResult;

                // Upgrade severity if rule-based found higher
                if (SeverityRank(ruleResult.SeverityLevel) > SeverityRank(result.SeverityLevel))
                {
                    _logger.LogWarning("Upgrading severity {Old} → {New}", result.SeverityLevel, ruleResult.SeverityLevel);
                    result.SeverityLevel = ruleResult.SeverityLevel;
                }

                // Fill missing fields from rule-based
                if (result.PeopleInvolved == 0 && ruleResult.PeopleInvolved > 0)
                    result.PeopleInvolved = ruleResult.PeopleInvolved;
                if (string.IsNullOrEmpty(result.Hazards) && !string.IsNullOrEmpty(ruleResult.Hazards))
                    result.Hazards = ruleResult.Hazards;

                // Specific type override: rule-based wins for flood/earthquake/riot/explosion
                // because AI sometimes collapses these to generic "hazard" or "crime"
                var specificTypes = new[] { "flood", "earthquake", "riot", "explosion", "missing_person" };
                if (specificTypes.Contains(ruleResult.IncidentType) &&
                    ruleResult.IncidentType != result.IncidentType)
                {
                    _logger.LogWarning("Specific type override: AI={Ai} → Rule={Rule}",
                        result.IncidentType, ruleResult.IncidentType);
                    result.IncidentType = ruleResult.IncidentType;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI unavailable — using rule-based fallback");
            sw.Stop();
            result = ruleResult;
        }

        if (string.IsNullOrEmpty(result.ReasoningSummary))
            result.ReasoningSummary =
                $"Incident classified as {result.IncidentType} with {result.SeverityLevel} severity." +
                (result.PeopleInvolved > 0 ? $" {result.PeopleInvolved} people involved." : "") +
                (string.IsNullOrEmpty(result.Hazards) ? "" : $" Hazards: {result.Hazards}.");

        return (result, new AgentStep
        {
            Agent      = "NlpTextAgent",
            Output     = $"type={result.IncidentType}, severity={result.SeverityLevel}",
            DurationMs = sw.ElapsedMilliseconds
        });
    }

    // ── Rule-based classifier ─────────────────────────────────────────────────
    // Order matters: most specific types first, generic "hazard" last.
    // Never use bare words like "road" or "street" as they appear in location names.

    public static NlpResult FallbackClassify(string text)
    {
        var t = text.ToLower();
        var result = new NlpResult();

        // ── Incident Type ─────────────────────────────────────────────────────

        if (ContainsAny(t, "heart attack", "cardiac arrest", "cardiac", "chest pain",
                           "not breathing", "unconscious", "collapsed", "seizure",
                           "stroke", "overdose", "fainted","dying","life threatening"))
            result.IncidentType = "medical";

        else if (ContainsAny(t, "earthquake", "tremor", "seismic", "ground shaking",
                                "ground shake", "building collapsed", "quake"))
            result.IncidentType = "earthquake";

        else if (ContainsAny(t, "flood", "flooding", "overflowed", "overflow",
                                "river burst", "flash flood", "water level", "submerged",
                                "inundated", "waterlogged"))
            result.IncidentType = "flood";

        else if (ContainsAny(t, "riot", "looting", "civil unrest", "mob violence",
                                "crowd violence", "protest violent", "mob attack"))
            result.IncidentType = "riot";

        else if (ContainsAny(t, "explosion", "exploded", "blast", "bomb", "detonation",
                                "blew up"))
            result.IncidentType = "explosion";

        else if (ContainsAny(t, "missing person", "missing child", "lost child",
                                "person missing", "child missing", "disappeared"))
            result.IncidentType = "missing_person";

        else if (ContainsAny(t, "fire", "flame", "burning", "blaze", "arson"))
            result.IncidentType = "fire";

        else if (ContainsAny(t,
                             "accident",
                             "crash",
                             "collision",
                             "highway accident",
                             "road accident",
                             "car crash",
                             "vehicle accident",
                             "pileup",
                             "pile-up",
                             "ran over",
                             "hit and run",
                             "head-on",
                             "drowining"))
            result.IncidentType = "accident";

        else if (ContainsAny(t, "injured", "injury", "injuries", "bleeding", "wound",
                                "hurt", "wounded", "ambulance needed" , "blood","medical help"))
            result.IncidentType = "medical";

        else if (ContainsAny(t, "gas leak", "gas pipe", "gas smell", "chemical spill",
                                "chemical leak", "toxic", "power line", "electricity leak",
                                "hazardous"))
            result.IncidentType = "hazard";

        else if (ContainsAny(t, "crime", "robbery", "theft", "shooting", "gun", "knife",
                                "assault", "murder", "attack", "burglary", "stolen", "suspect", "armed"))
            result.IncidentType = "crime";

        else
            result.IncidentType = "unknown";

        // ── Severity ──────────────────────────────────────────────────────────

        bool isCritical = ContainsAny(t,
            "earthquake", "riot", "looting", "mob", "flash flood", "families trapped",
            "people trapped", "rooftops", "houses flooded", "explosion", "bomb",
            "100", "50", "many people", "dozens", "mass", "urgent", "immediately",
            "not breathing", "cardiac arrest", "heart attack", "critical", "trapped",
            "rescue needed", "life threatening", "dying");

        bool isHigh = ContainsAny(t,
            "injured", "injury", "injuries", "serious", "severe", "unconscious",
            "bleeding", "multiple", "crash", "collision", "hurt", "spreading",
            "several", "flooding", "submerged", "evacuating", "overflowed");

        result.SeverityLevel = isCritical ? "critical"
                             : isHigh     ? "high"
                             : ContainsAny(t, "accident", "smoke", "damage", "fire",
                                              "help", "emergency", "gas", "possible") ? "medium"
                             : "low";

        // ── People count ──────────────────────────────────────────────────────

        if      (t.Contains("100 people") || t.Contains("hundred"))   result.PeopleInvolved = 100;
        else if (t.Contains("50 people")  || t.Contains("fifty"))     result.PeopleInvolved = 50;
        else if (ContainsAny(t, "many people", "dozens", "mass", "more than 20",
                                "families", "residents", "more than 30"))        result.PeopleInvolved = 20;
        else if (ContainsAny(t, "several", "group", "more than 5"))              result.PeopleInvolved = 5;
        else if (ContainsAny(t, "my friend", "someone", "a person", "a man",
                                "a woman", "a child", "a driver"))               result.PeopleInvolved = 1;

        // ── Vehicles ──────────────────────────────────────────────────────────

        if      (ContainsAny(t, "cars", "vehicles", "trucks", "multiple cars"))  result.VehiclesInvolved = 3;
        else if (ContainsAny(t, "car", "vehicle", "truck", "motorcycle", "bus")) result.VehiclesInvolved = 1;

        // ── Hazards ───────────────────────────────────────────────────────────

        var hazards = new List<string>();
        if (ContainsAny(t, "flame", "fire", "burning", "on fire", "in flames")) hazards.Add("flames");
        if (t.Contains("smoke"))                                                 hazards.Add("smoke");
        if (ContainsAny(t, "gas leak", "gas pipe", "gas smell"))                hazards.Add("gas leak");
        if (ContainsAny(t, "flood", "flooding", "submerged", "overflowed"))     hazards.Add("flooding");
        if (ContainsAny(t, "chemical", "toxic", "hazardous"))                   hazards.Add("chemical");
        if (ContainsAny(t, "explosion", "blast", "bomb"))                       hazards.Add("explosion");
        if (ContainsAny(t, "fuel leak", "petrol leak", "oil spill"))            hazards.Add("fuel leak");
        result.Hazards = string.Join(", ", hazards);

        result.ReasoningSummary =
            $"Rule-based: {result.IncidentType} with {result.SeverityLevel} severity." +
            (result.PeopleInvolved > 0 ? $" {result.PeopleInvolved} people involved." : "") +
            (string.IsNullOrEmpty(result.Hazards) ? "" : $" Hazards: {result.Hazards}.");

        return result;
    }

    private static int SeverityRank(string s) => s switch
    {
        "critical" => 4, "high" => 3, "medium" => 2, "low" => 1, _ => 0
    };

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k));
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