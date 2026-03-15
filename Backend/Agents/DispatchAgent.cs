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

        AVAILABLE UNITS:
          police, traffic_police, ambulance, fire, tow_truck, utility,
          rescue, coast_guard, bomb_squad, hazmat, national_guard, riot_control,
          flood_rescue, earthquake_response, k9_unit, drone_unit

        DISPATCH RULES — add all units that apply, never under-dispatch:

        fire          → fire, ambulance
                        + traffic_police, tow_truck if on road or highway
                        + police if critical or arson suspected
                        + hazmat if chemical fire or fuel involved
                        + drone_unit if wildfire or large area

        explosion     → fire, ambulance, police, bomb_squad, hazmat
                        + national_guard if mass casualty or terrorism suspected
                        + rescue if people trapped

        accident      → police, traffic_police, ambulance, tow_truck
                        + fire if flames, fuel leak, or vehicle on fire
                        + rescue if people trapped in vehicle
                        + hazmat if fuel spill or chemical tanker

        medical       → ambulance
                        + police if critical, many people, crime related, or public place
                        + fire if person trapped or building involved

        crime         → police
                        + ambulance if any injuries
                        + k9_unit if suspect fled on foot or search needed
                        + drone_unit if large area search or active pursuit

        flood         → rescue, flood_rescue, utility, ambulance, police
                        + coast_guard if coastal, river, or large water body involved
                        + national_guard if large scale or mass evacuation
                        + drone_unit if aerial search needed
                        + helicopter if people stranded on rooftops

        earthquake    → rescue, earthquake_response, ambulance, fire, police, utility
                        + national_guard if widespread structural collapse
                        + hazmat if industrial area or chemical plant affected
                        + drone_unit for aerial damage assessment

        riot          → police, riot_control, ambulance
                        + national_guard if critical scale or armed
                        + fire if arson or fires started

        hazard        → utility, fire
                        + hazmat if chemical, toxic, radiation, or biohazard
                        + rescue if people trapped or injured nearby
                        + police if public area needs cordoning
                        + ambulance if any exposure or injuries

        missing_person → police
                        + k9_unit if search in outdoor/rural area
                        + drone_unit if large area search

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

        // ── Incident type detection ───────────────────────────────────────────

        bool isAccident = ContainsAny(c,
            "accident", "crash", "collision", "car crash", "pileup",
            "hit and run", "head-on", "overturned", "vehicle rollover",
            "motorcycle crash", "bus accident", "pedestrian hit", "fender bender");

        bool isFire = ContainsAny(c,
            "fire", "flame", "burning", "blaze", "arson", "wildfire",
            "bushfire", "structure fire", "house fire", "engulfed");

        bool isExplosion = ContainsAny(c,
            "explosion", "explode", "blast", "bomb", "detonation", "blew up",
            "gas explosion", "ied", "detonated", "fireworks explosion");

        bool isMedical = ContainsAny(c,
            "medical", "heart attack", "cardiac", "injured", "injuries",
            "unconscious", "bleeding", "not breathing", "seizure", "stroke",
            "hurt", "wounded", "overdose", "fainted", "unresponsive",
            "person down", "breathing difficulty", "choking", "drowning",
            "broken bone", "head injury", "allergic reaction", "diabetic");

        bool isCrime = ContainsAny(c,
            "crime", "robbery", "shooting", "assault", "murder", "gun", "knife",
            "attack", "theft", "armed", "kidnapping", "hostage", "carjacking",
            "vandalism", "domestic violence", "gang", "break-in", "mugging",
            "home invasion", "suspect");

        bool isFlood = ContainsAny(c,
            "flood", "flooding", "overflowed", "flash flood", "submerged",
            "water level", "river burst", "inundated", "waterlogged",
            "storm surge", "dam break", "swept away", "road underwater",
            "cars floating", "drainage overflow", "street flooded",
            "basement flooded", "tidal flood");

        bool isEarthquake = ContainsAny(c,
            "earthquake", "tremor", "seismic", "quake", "ground shaking",
            "aftershock", "landslide", "sinkhole", "structural collapse",
            "walls cracking", "building crumbling", "rubble");

        bool isRiot = ContainsAny(c,
            "riot", "looting", "civil unrest", "mob", "crowd violence",
            "mass brawl", "public disorder", "clashes", "demonstrators violent",
            "property destruction", "tear gas", "barricades", "unrest");

        bool isHazard = ContainsAny(c,
            "gas leak", "chemical", "toxic", "power line", "hazardous",
            "live wire", "radiation", "biohazard", "nuclear", "oil spill",
            "fuel leak", "pipeline leak", "industrial accident", "factory leak");

        bool isMissing = ContainsAny(c,
            "missing", "lost child", "disappeared", "abducted",
            "amber alert", "silver alert", "person not found", "search and rescue");

        // ── Contextual flags ──────────────────────────────────────────────────

        bool onRoad       = ContainsAny(c, "highway", "motorway", "traffic", "junction",
                                           "lane", "road", "street", "intersection");
        bool hasFlames    = ContainsAny(c, "flame", "flaming", "on fire", "in flames",
                                           "burning", "engulfed", "wildfire", "blaze");
        bool hasInjured   = ContainsAny(c, "injur", "hurt", "wounded", "bleeding",
                                           "unconscious", "not breathing", "unresponsive");
        bool isCritical   = ContainsAny(c, "critical", "many people", "urgent", "trapped",
                                           "mass", "dozens", "50", "100", "life threatening",
                                           "dying", "rescue needed", "swept away",
                                           "rooftop", "families trapped");
        bool isLargeScale = ContainsAny(c, "large scale", "widespread", "entire",
                                           "neighborhood", "city", "town", "district",
                                           "evacuation", "mass evacuation");
        bool nearWater    = ContainsAny(c, "river", "coast", "sea", "ocean", "lake",
                                           "canal", "harbour", "port", "beach");
        bool hasChemical  = ContainsAny(c, "chemical", "toxic", "hazardous", "radiation",
                                           "biohazard", "nuclear", "industrial");
        bool suspectFled  = ContainsAny(c, "fled", "running", "escaped", "on foot",
                                           "pursuit", "chase");
        bool largeAreaSearch = ContainsAny(c, "large area", "forest", "rural", "mountain",
                                              "wilderness", "open field", "wide area");
        bool arsonSuspected  = ContainsAny(c, "arson", "deliberately", "suspicious fire",
                                              "set on fire");

        // ── Unit assignment ───────────────────────────────────────────────────

        if (isAccident)
        {
            units.Add("police"); units.Add("ambulance"); units.Add("tow_truck");
            if (onRoad)        units.Add("traffic_police");
            if (hasFlames)     { units.Add("fire"); units.Add("hazmat"); }
            if (isCritical)    units.Add("rescue");
        }

        if (isFire)
        {
            units.Add("fire"); units.Add("ambulance");
            if (onRoad)           { units.Add("traffic_police"); units.Add("tow_truck"); }
            if (isCritical)       units.Add("police");
            if (hasChemical)      units.Add("hazmat");
            if (arsonSuspected)   units.Add("police");
            if (isLargeScale)     units.Add("drone_unit");
        }

        if (isExplosion)
        {
            units.Add("fire"); units.Add("ambulance");
            units.Add("police"); units.Add("bomb_squad"); units.Add("hazmat");
            if (isCritical)    units.Add("national_guard");
            if (isCritical)    units.Add("rescue");
        }

        if (isMedical)
        {
            units.Add("ambulance");
            if (isCritical || isLargeScale) units.Add("police");
        }

        if (isCrime)
        {
            units.Add("police");
            if (hasInjured)    units.Add("ambulance");
            if (suspectFled)   units.Add("k9_unit");
            if (largeAreaSearch || isLargeScale) units.Add("drone_unit");
        }

        if (isFlood)
        {
            units.Add("rescue"); units.Add("flood_rescue");
            units.Add("utility"); units.Add("ambulance"); units.Add("police");
            if (nearWater || isCritical) units.Add("coast_guard");
            if (isLargeScale)  units.Add("national_guard");
            if (isLargeScale)  units.Add("drone_unit");
        }

        if (isEarthquake)
        {
            units.Add("rescue"); units.Add("earthquake_response");
            units.Add("ambulance"); units.Add("fire");
            units.Add("police"); units.Add("utility");
            if (isLargeScale)  units.Add("national_guard");
            if (hasChemical)   units.Add("hazmat");
            if (isLargeScale)  units.Add("drone_unit");
        }

        if (isRiot)
        {
            units.Add("police"); units.Add("riot_control"); units.Add("ambulance");
            if (isCritical)    units.Add("national_guard");
            if (hasFlames)     units.Add("fire");
        }

        if (isHazard)
        {
            units.Add("utility"); units.Add("fire");
            if (hasChemical)   units.Add("hazmat");
            if (hasInjured)    units.Add("ambulance");
            if (isCritical || isLargeScale) units.Add("police");
        }

        if (isMissing)
        {
            units.Add("police");
            if (largeAreaSearch) { units.Add("k9_unit"); units.Add("drone_unit"); }
        }

        // ── Always add traffic_police for traffic congestion reports ──────────
        if (ContainsAny(c, "traffic jam", "blocked road", "congestion", "road blocked"))
            units.Add("traffic_police");

        if (units.Count == 0) units.Add("police");

        // ── Priority ──────────────────────────────────────────────────────────
        int priority = isCritical || isExplosion || isEarthquake || isRiot ? 1
            : isFlood || hasInjured || isFire || ContainsAny(c, "serious", "severe",
              "significant", "multiple", "several injured")                 ? 2
            : ContainsAny(c, "medium", "possible", "minor", "accident")    ? 3
            : 4;

        return new DispatchRecommendation { UnitsRequired = units.ToList(), Priority = priority };
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k));
}