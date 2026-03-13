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
        - "fire"     → fire, flames, burning, blaze, explosion, cars on fire
        - "accident" → car crash, vehicle collision, highway accident, road accident, cars flipping
        - "medical"  → heart attack, cardiac arrest, injury, bleeding, unconscious, seizure,
                       hurt people, injured people, not breathing, fainted, stroke, overdose
        - "crime"    → robbery, shooting, assault, theft, murder, stabbing, violence, gun, knife
        - "hazard"   → gas leak, flooding, flood, overflowed river, chemical spill, downed power line, toxic
        - "unknown"  → ONLY if absolutely none of the above apply

        SEVERITY — pick exactly one:
        - "critical" → 10+ people injured OR cardiac arrest OR not breathing OR explosion OR
                       people trapped OR cars on fire with people OR urgent OR mass casualty OR
                       families trapped OR houses flooded
        - "high"     → confirmed injuries, multiple people hurt, serious accident,
                       vehicle fire, unconscious person, several injured, spreading fire
        - "medium"   → possible injuries, contained incident, minor accident, small fire
        - "low"      → no injuries, no immediate danger, minor issue only

        IMPORTANT: If report mentions "many people", "50 people", "100 people", "several injured",
        "trapped", "urgent", "need help immediately" — severity MUST be critical or high. NEVER low.

        EXAMPLES — follow these exactly:
        "accident on highway 50 people injured cars on fire need urgent help"
        → incident_type=accident, severity=critical, people_involved=50, vehicles_involved=3, hazards="flames, smoke"

        "my friend had a heart attack"
        → incident_type=medical, severity=critical, people_involved=1, hazards=""

        "the river overflowed and families are trapped on rooftops"
        → incident_type=hazard, severity=critical, people_involved=5, hazards="flooding"

        "gas pipe burst strong smell people evacuating"
        → incident_type=hazard, severity=high, people_involved=0, hazards="gas leak"

        "there is a fire in the building people are trapped"
        → incident_type=fire, severity=critical, people_involved=2, hazards="flames, smoke"

        "car crash on main road driver unconscious"
        → incident_type=accident, severity=high, people_involved=1, vehicles_involved=1, hazards=""

        "small kitchen fire no injuries"
        → incident_type=fire, severity=medium, people_involved=0, hazards="smoke"

        Respond ONLY with this exact JSON structure, no markdown, no extra text:
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

        // Always run rule-based first as a safety baseline
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

                // Safety net: if rule-based found HIGHER severity, always upgrade
                if (SeverityRank(ruleResult.SeverityLevel) > SeverityRank(result.SeverityLevel))
                {
                    _logger.LogWarning("AI underestimated severity {Old} → upgrading to {New}",
                        result.SeverityLevel, ruleResult.SeverityLevel);
                    result.SeverityLevel = ruleResult.SeverityLevel;
                }

                // Fill missing people count from rule-based
                if (result.PeopleInvolved == 0 && ruleResult.PeopleInvolved > 0)
                    result.PeopleInvolved = ruleResult.PeopleInvolved;

                // Fill missing hazards from rule-based
                if (string.IsNullOrEmpty(result.Hazards) && !string.IsNullOrEmpty(ruleResult.Hazards))
                    result.Hazards = ruleResult.Hazards;

                // If rule-based strongly disagrees on type (e.g. AI says accident but
                // rule-based says hazard because of flood keywords) — trust rule-based
                if (ruleResult.IncidentType != "unknown" &&
                    ruleResult.IncidentType != result.IncidentType)
                {
                    // Hazard and medical take priority over accident/fire misclassifications
                    if (ruleResult.IncidentType == "hazard" || ruleResult.IncidentType == "medical")
                    {
                        _logger.LogWarning("AI type mismatch: AI={Ai}, Rule={Rule} — trusting rule-based",
                            result.IncidentType, ruleResult.IncidentType);
                        result.IncidentType = ruleResult.IncidentType;
                    }
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
                $"{(result.PeopleInvolved > 0 ? $" {result.PeopleInvolved} people involved." : "")}" +
                $"{(string.IsNullOrEmpty(result.Hazards) ? "" : $" Hazards: {result.Hazards}.")}";

        return (result, new AgentStep
        {
            Agent      = "NlpTextAgent",
            Output     = $"type={result.IncidentType}, severity={result.SeverityLevel}",
            DurationMs = sw.ElapsedMilliseconds
        });
    }

    // ── Rule-based fallback classifier ────────────────────────────────────────
    // IMPORTANT: Order of checks matters — most specific conditions first.
    // Never use bare generic words like "road" or "street" as they appear in
    // location names (e.g. "Riverside Road") and cause misclassification.

    public static NlpResult FallbackClassify(string text)
    {
        var t = text.ToLower();
        var result = new NlpResult();

        // ── Incident Type ─────────────────────────────────────────────────────

        // 1. Medical — check first because "injured" could also match accident
        if (ContainsAny(t, "heart attack", "cardiac arrest", "cardiac", "chest pain",
                           "not breathing", "unconscious", "collapsed", "seizure",
                           "stroke", "overdose", "fainted"))
            result.IncidentType = "medical";

        // 2. Hazard — check before accident to avoid "Riverside Road" → accident
        else if (ContainsAny(t, "flood", "flooding", "overflowed", "overflow",
                                "river burst", "water level", "submerged",
                                "gas leak", "gas pipe", "gas smell",
                                "chemical spill", "chemical leak", "toxic",
                                "power line down", "downed wire", "electricity leak",
                                "hazardous"))
            result.IncidentType = "hazard";

        // 3. Fire
        else if (ContainsAny(t, "fire", "flame", "burning", "blaze", "explosion", "explode"))
            result.IncidentType = "fire";

        // 4. Accident — only specific accident phrases, NOT bare "road" or "street"
        else if (ContainsAny(t, "accident", "crash", "collision",
                                "highway accident", "road accident", "car crash",
                                "vehicle accident", "cars flipped", "cars flipping",
                                "truck accident", "pileup", "pile-up", "ran over",
                                "hit and run", "head-on"))
            result.IncidentType = "accident";

        // 5. Injuries without clearer context → medical
        else if (ContainsAny(t, "injured", "injury", "injuries", "bleeding", "wound",
                                "hurt", "wounded", "ambulance needed", "need ambulance"))
            result.IncidentType = "medical";

        // 6. Vehicle presence without accident keyword → accident
        else if (ContainsAny(t, "vehicle", "car", "truck", "motorcycle", "bus")
                 && ContainsAny(t, "traffic", "highway", "motorway", "speeding", "reckless"))
            result.IncidentType = "accident";

        // 7. Crime
        else if (ContainsAny(t, "crime", "robbery", "theft", "shooting", "gun", "knife",
                                "assault", "murder", "attack", "burglary", "stolen",
                                "suspect", "armed"))
            result.IncidentType = "crime";

        else
            result.IncidentType = "unknown";

        // ── Severity ──────────────────────────────────────────────────────────

        bool isCritical = ContainsAny(t,
            "100", "50", "many people", "dozens", "mass casualty", "more than 20",
            "not breathing", "cardiac arrest", "heart attack", "trapped", "explosion",
            "flaming", "cars are in flames", "on fire", "urgent", "immediately",
            "life threatening", "critical", "dying", "dead", "rooftop", "rooftops",
            "families trapped", "people trapped", "houses flooded", "rescue");

        bool isHigh = ContainsAny(t,
            "injured", "injury", "injuries", "serious", "severe", "unconscious",
            "bleeding", "multiple", "crash", "collision", "hurt", "hospital",
            "spreading", "several", "affected", "people hurt", "more than 5",
            "evacuating", "evacuation", "submerged", "overflowed");

        if (isCritical)
            result.SeverityLevel = "critical";
        else if (isHigh)
            result.SeverityLevel = "high";
        else if (ContainsAny(t, "accident", "smoke", "damage", "help", "emergency",
                                "fire", "possible", "suspicious", "gas", "flooding"))
            result.SeverityLevel = "medium";
        else
            result.SeverityLevel = "low";

        // ── People count ──────────────────────────────────────────────────────

        if      (t.Contains("100 people") || t.Contains("hundred people"))       result.PeopleInvolved = 100;
        else if (t.Contains("50 people")  || t.Contains("fifty people"))         result.PeopleInvolved = 50;
        else if (t.Contains("100") && ContainsAny(t, "injur", "affect", "hurt")) result.PeopleInvolved = 100;
        else if (t.Contains("50")  && ContainsAny(t, "injur", "affect", "hurt")) result.PeopleInvolved = 50;
        else if (ContainsAny(t, "many people", "dozens", "multiple people",
                                "mass", "more than 20", "more than 30",
                                "families", "several families", "residents"))     result.PeopleInvolved = 20;
        else if (ContainsAny(t, "several", "few people", "group", "affected",
                                "more than 5", "more than 10"))                   result.PeopleInvolved = 5;
        else if (ContainsAny(t, "my friend", "someone", "a person", "a man",
                                "a woman", "a child", "a driver", "a passenger")) result.PeopleInvolved = 1;

        // ── Vehicles ──────────────────────────────────────────────────────────

        if      (ContainsAny(t, "cars", "vehicles", "trucks", "multiple cars"))   result.VehiclesInvolved = 3;
        else if (ContainsAny(t, "car", "vehicle", "truck", "motorcycle", "bus"))  result.VehiclesInvolved = 1;

        // ── Hazards ───────────────────────────────────────────────────────────

        var hazards = new List<string>();
        if (ContainsAny(t, "flame", "flaming", "fire", "burning", "cars are in flames",
                           "on fire"))
            hazards.Add("flames");
        if (t.Contains("smoke"))
            hazards.Add("smoke");
        if (ContainsAny(t, "gas leak", "gas pipe", "gas smell"))
            hazards.Add("gas leak");
        if (ContainsAny(t, "flood", "flooding", "overflowed", "submerged", "water level"))
            hazards.Add("flooding");
        if (ContainsAny(t, "chemical", "toxic", "spill", "hazardous material"))
            hazards.Add("chemical spill");
        if (ContainsAny(t, "explosion", "explode"))
            hazards.Add("explosion risk");
        if (ContainsAny(t, "fuel leak", "fuel spill", "petrol leak", "oil spill"))
            hazards.Add("fuel leak");
        result.Hazards = string.Join(", ", hazards);

        result.ReasoningSummary =
            $"Rule-based: {result.IncidentType} with {result.SeverityLevel} severity." +
            $"{(result.PeopleInvolved > 0 ? $" {result.PeopleInvolved} people involved." : "")}" +
            $"{(string.IsNullOrEmpty(result.Hazards) ? "" : $" Hazards: {result.Hazards}.")}";

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