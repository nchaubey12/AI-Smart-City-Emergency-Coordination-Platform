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
        - "fire"           → fire, flames, burning, blaze, arson, wildfire, bushfire, structure fire,
                             house fire, car fire, building on fire, smoke rising, engulfed in flames
        - "explosion"      → explosion, blast, bomb, detonation, blew up, exploded, gas explosion,
                             building exploded, boom, detonated, improvised device, IED, fireworks explosion
        - "accident"       → car crash, vehicle collision, highway accident, road accident, pileup,
                             hit and run, head-on collision, ran over, overturned vehicle, truck accident,
                             motorcycle crash, bus accident, pedestrian hit, vehicle rollover, fender bender
        - "medical"        → heart attack, cardiac arrest, chest pain, not breathing, unconscious,
                             collapsed, seizure, stroke, overdose, fainted, injured, bleeding, wound,
                             hurt, ambulance needed, medical help, breathing difficulty, allergic reaction,
                             diabetic emergency, choking, drowning, fall injury, broken bone, head injury,
                             life threatening, dying, person down, unresponsive, mental health crisis
        - "crime"          → robbery, shooting, assault, theft, murder, stabbing, violence, gun, knife,
                             attack, burglary, stolen, suspect, armed, kidnapping, hostage, carjacking,
                             vandalism, domestic violence, sexual assault, drug dealing, gang activity,
                             threatening, harassment, break-in, home invasion, shoplifting, mugging
        - "flood"          → flooding, flood, overflowed river, flash flood, water level rising, submerged,
                             inundated, waterlogged, river burst, storm surge, tidal flooding, dam break,
                             sewage overflow, water on road, road underwater, cars floating, swept away,
                             drainage overflow, heavy rain flooding, basement flooded, street flooded
        - "earthquake"     → earthquake, tremor, seismic, ground shaking, ground shake, building collapsed,
                             quake, aftershock, landslide, sink hole, structural collapse, walls cracking,
                             ground cracking, building crumbling, rubble, debris from quake
        - "riot"           → riot, civil unrest, mob, looting, protest turned violent, crowd violence,
                             mob attack, mass brawl, public disorder, clashes, demonstrators violent,
                             property destruction, tear gas, barricades, police confrontation, unrest
        - "hazard"         → gas leak, chemical spill, downed power line, toxic, hazardous material,
                             electricity leak, live wire, radiation, biohazard, nuclear, oil spill,
                             fuel leak, pipeline leak, industrial accident, factory leak, smoke without fire,
                             fallen tree blocking road, sinkhole, structural danger, bridge damage
                             (use ONLY when no more specific type fits)
        - "missing_person" → missing person, lost child, person not found, disappeared, abducted,
                             child lost, elderly missing, person not returned, search and rescue needed,
                             amber alert, silver alert
        - "unknown"        → ONLY if absolutely none of the above apply

        SEVERITY — pick exactly one:
        - "critical" → 10+ people injured OR cardiac arrest OR not breathing OR explosion OR
                       people trapped OR urgent OR mass casualty OR families trapped OR houses flooded
                       OR earthquake OR riot with violence OR flash flood OR dam break OR swept away
                       OR building collapse OR not breathing OR unresponsive OR bomb OR hostage
        - "high"     → confirmed injuries, serious accident, unconscious person, spreading fire,
                       several injured, significant flooding, multiple vehicles, armed crime,
                       large fire, gas leak near people, chemical exposure, river overflowing
        - "medium"   → possible injuries, contained incident, minor accident, small fire,
                       suspicious activity, minor flooding, smoke reported, fender bender,
                       non-life-threatening injury, verbal altercation
        - "low"      → no injuries, no immediate danger, minor issue, noise complaint,
                       minor property damage, stray animal, non-urgent request

        IMPORTANT:
        - flood, earthquake, riot, explosion are almost always critical or high — NEVER low
        - When people are trapped or swept away always use critical
        - When in doubt between two levels choose the higher one

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
    // Order matters: most specific / highest-stakes types first, generic last.

    public static NlpResult FallbackClassify(string text)
    {
        var t = text.ToLower();
        var result = new NlpResult();

        // ── Incident Type ─────────────────────────────────────────────────────

        if (ContainsAny(t,
                "heart attack", "cardiac arrest", "cardiac", "chest pain",
                "not breathing", "unconscious", "collapsed", "seizure", "stroke",
                "overdose", "fainted", "dying", "life threatening", "unresponsive",
                "person down", "breathing difficulty", "allergic reaction",
                "diabetic emergency", "choking", "drowning", "fall injury",
                "broken bone", "head injury", "mental health crisis"))
            result.IncidentType = "medical";

        else if (ContainsAny(t,
                "earthquake", "tremor", "seismic", "ground shaking", "ground shake",
                "building collapsed", "quake", "aftershock", "landslide", "sinkhole",
                "structural collapse", "walls cracking", "ground cracking",
                "building crumbling", "rubble", "debris from quake"))
            result.IncidentType = "earthquake";

        else if (ContainsAny(t,
                "flood", "flooding", "overflowed", "overflow", "river burst",
                "flash flood", "water level", "submerged", "inundated", "waterlogged",
                "storm surge", "tidal flood", "dam break", "sewage overflow",
                "road underwater", "cars floating", "swept away", "drainage overflow",
                "heavy rain flooding", "basement flooded", "street flooded"))
            result.IncidentType = "flood";

        else if (ContainsAny(t,
                "riot", "looting", "civil unrest", "mob violence", "crowd violence",
                "protest violent", "mob attack", "mass brawl", "public disorder",
                "clashes", "demonstrators violent", "property destruction",
                "tear gas", "barricades", "police confrontation", "unrest"))
            result.IncidentType = "riot";

        else if (ContainsAny(t,
                "explosion", "exploded", "blast", "bomb", "detonation", "blew up",
                "gas explosion", "building exploded", "boom", "detonated",
                "improvised device", "ied", "fireworks explosion"))
            result.IncidentType = "explosion";

        else if (ContainsAny(t,
                "missing person", "missing child", "lost child", "person missing",
                "child missing", "disappeared", "abducted", "elderly missing",
                "person not returned", "amber alert", "silver alert",
                "search and rescue needed"))
            result.IncidentType = "missing_person";

        else if (ContainsAny(t,
                "fire", "flame", "burning", "blaze", "arson", "wildfire",
                "bushfire", "structure fire", "house fire", "car fire",
                "building on fire", "engulfed in flames"))
            result.IncidentType = "fire";

        else if (ContainsAny(t,
                "accident", "crash", "collision", "highway accident", "road accident",
                "car crash", "vehicle accident", "pileup", "pile-up", "ran over",
                "hit and run", "head-on", "overturned vehicle", "truck accident",
                "motorcycle crash", "bus accident", "pedestrian hit",
                "vehicle rollover", "fender bender"))
            result.IncidentType = "accident";

        else if (ContainsAny(t,
                "injured", "injury", "injuries", "bleeding", "wound", "hurt",
                "wounded", "ambulance needed", "blood", "medical help",
                "broken bone", "head injury"))
            result.IncidentType = "medical";

        else if (ContainsAny(t,
                "gas leak", "gas pipe", "gas smell", "chemical spill", "chemical leak",
                "toxic", "power line", "electricity leak", "hazardous", "live wire",
                "radiation", "biohazard", "nuclear", "oil spill", "fuel leak",
                "pipeline leak", "industrial accident", "factory leak",
                "fallen tree blocking", "structural danger", "bridge damage"))
            result.IncidentType = "hazard";

        else if (ContainsAny(t,
                "crime", "robbery", "theft", "shooting", "gun", "knife", "assault",
                "murder", "attack", "burglary", "stolen", "suspect", "armed",
                "kidnapping", "hostage", "carjacking", "vandalism",
                "domestic violence", "sexual assault", "drug dealing", "gang",
                "threatening", "harassment", "break-in", "home invasion",
                "shoplifting", "mugging"))
            result.IncidentType = "crime";

        else
            result.IncidentType = "unknown";

        // ── Severity ──────────────────────────────────────────────────────────

        bool isCritical = ContainsAny(t,
            "earthquake", "riot", "looting", "mob", "flash flood", "families trapped",
            "people trapped", "rooftops", "houses flooded", "explosion", "bomb",
            "dam break", "swept away", "building collapse", "building collapsed",
            "100", "50", "many people", "dozens", "mass", "urgent", "immediately",
            "not breathing", "cardiac arrest", "heart attack", "critical", "trapped",
            "rescue needed", "life threatening", "dying", "unresponsive",
            "hostage", "armed gunman", "active shooter", "mass casualty",
            "building on fire", "engulfed", "wildfire spreading", "ied", "detonated");

        bool isHigh = ContainsAny(t,
            "injured", "injury", "injuries", "serious", "severe", "unconscious",
            "bleeding", "multiple", "crash", "collision", "hurt", "spreading",
            "several", "flooding", "submerged", "evacuating", "overflowed",
            "armed", "significant", "river burst", "structural collapse",
            "gas leak near", "chemical exposure", "large fire", "spreading fire",
            "multiple vehicles", "vehicle rollover", "bus accident");

        result.SeverityLevel = isCritical ? "critical"
                             : isHigh     ? "high"
                             : ContainsAny(t,
                                 "accident", "smoke", "damage", "fire", "help",
                                 "emergency", "gas", "possible", "minor flooding",
                                 "suspicious", "fender bender", "verbal", "reported")
                                            ? "medium"
                             : "low";

        // ── People count ──────────────────────────────────────────────────────

        if      (t.Contains("100 people") || t.Contains("hundred"))
            result.PeopleInvolved = 100;
        else if (t.Contains("50 people") || t.Contains("fifty"))
            result.PeopleInvolved = 50;
        else if (ContainsAny(t, "many people", "dozens", "mass", "more than 20",
                                "families", "residents", "more than 30", "crowd",
                                "multiple people", "large group"))
            result.PeopleInvolved = 20;
        else if (ContainsAny(t, "several", "group", "more than 5", "a few people",
                                "handful"))
            result.PeopleInvolved = 5;
        else if (ContainsAny(t, "my friend", "someone", "a person", "a man",
                                "a woman", "a child", "a driver", "a cyclist",
                                "a pedestrian", "an elderly", "a passenger"))
            result.PeopleInvolved = 1;

        // ── Vehicles ──────────────────────────────────────────────────────────

        if      (ContainsAny(t, "multiple cars", "several cars", "many vehicles",
                                "multiple vehicles", "cars", "trucks"))
            result.VehiclesInvolved = 3;
        else if (ContainsAny(t, "car", "vehicle", "truck", "motorcycle", "bus",
                                "van", "lorry", "suv", "bicycle", "scooter",
                                "ambulance", "fire truck", "tram"))
            result.VehiclesInvolved = 1;

        // ── Hazards ───────────────────────────────────────────────────────────

        var hazards = new List<string>();
        if (ContainsAny(t, "flame", "fire", "burning", "on fire", "in flames",
                           "blaze", "wildfire", "engulfed"))          hazards.Add("flames");
        if (ContainsAny(t, "smoke", "smog", "fumes"))                 hazards.Add("smoke");
        if (ContainsAny(t, "gas leak", "gas pipe", "gas smell",
                           "gas explosion"))                           hazards.Add("gas leak");
        if (ContainsAny(t, "flood", "flooding", "submerged",
                           "overflowed", "flash flood", "swept away",
                           "water level", "inundated", "dam break"))  hazards.Add("flooding");
        if (ContainsAny(t, "chemical", "toxic", "hazardous",
                           "biohazard", "radiation", "nuclear",
                           "industrial leak"))                         hazards.Add("chemical");
        if (ContainsAny(t, "explosion", "blast", "bomb", "ied",
                           "detonation"))                              hazards.Add("explosion");
        if (ContainsAny(t, "fuel leak", "petrol leak", "oil spill",
                           "pipeline leak"))                           hazards.Add("fuel leak");
        if (ContainsAny(t, "power line", "live wire", "electricity leak",
                           "downed wire"))                             hazards.Add("downed wires");
        if (ContainsAny(t, "fallen tree", "debris", "rubble",
                           "structural collapse"))                     hazards.Add("debris");
        if (ContainsAny(t, "landslide", "mudslide", "sinkhole"))      hazards.Add("landslide");
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