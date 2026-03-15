using System.Text.Json.Serialization;

namespace EmergencyPlatform.Frontend.Models;

public class IncidentReportViewModel
{
    [JsonPropertyName("incident_id")]
    public string IncidentId { get; set; } = string.Empty;

    [JsonPropertyName("input_type")]
    public string InputType { get; set; } = string.Empty;

    [JsonPropertyName("incident_type")]
    public string IncidentType { get; set; } = string.Empty;

    [JsonPropertyName("severity_level")]
    public string SeverityLevel { get; set; } = string.Empty;

    [JsonPropertyName("location_data")]
    public LocationDataViewModel LocationData { get; set; } = new();

    [JsonPropertyName("entities")]
    public EntityDataViewModel Entities { get; set; } = new();

    [JsonPropertyName("reasoning_summary")]
    public string ReasoningSummary { get; set; } = string.Empty;

    [JsonPropertyName("dispatch_recommendation")]
    public DispatchRecommendationViewModel DispatchRecommendation { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("agent_trace")]
    public List<AgentStepViewModel> AgentTrace { get; set; } = new();

    // ── UI helpers ────────────────────────────────────────────────────────
    // Normalize to lowercase so switches always match regardless of AI casing.

    public string SeverityColor => SeverityLevel.Trim().ToLower() switch
    {
        "critical" => "#ff2d55",
        "high"     => "#ff9500",
        "medium"   => "#ffd60a",
        "low"      => "#30d158",
        _          => "#636366"
    };

    public string IncidentIcon => IncidentType.Trim().ToLower() switch
    {
        "fire"           => "🔥",
        "accident"       => "🚗",
        "medical"        => "🚑",
        "crime"          => "🚔",
        "hazard"         => "⚠️",
        "flood"          => "🌊",
        "earthquake"     => "🏚️",
        "riot"           => "👥",
        "explosion"      => "💥",
        "missing_person" => "🔍",
        _                => "❓"
    };

    public string PriorityLabel => DispatchRecommendation.Priority switch
    {
        1 => "IMMEDIATE",
        2 => "URGENT",
        3 => "NORMAL",
        4 => "LOW",
        5 => "ROUTINE",
        _ => "UNKNOWN"
    };
}

public class LocationDataViewModel
{
    [JsonPropertyName("lat")]
    public double? Lat { get; set; }

    [JsonPropertyName("lon")]
    public double? Lon { get; set; }

    [JsonPropertyName("inferred_location_description")]
    public string InferredLocationDescription { get; set; } = string.Empty;
}

public class EntityDataViewModel
{
    [JsonPropertyName("people_involved")]
    public int PeopleInvolved { get; set; }

    [JsonPropertyName("vehicles_involved")]
    public int VehiclesInvolved { get; set; }

    [JsonPropertyName("hazards")]
    public string Hazards { get; set; } = string.Empty;
}

public class DispatchRecommendationViewModel
{
    [JsonPropertyName("units_required")]
    public List<string> UnitsRequired { get; set; } = new();

    [JsonPropertyName("priority")]
    public int Priority { get; set; }
}

public class AgentStepViewModel
{
    [JsonPropertyName("agent")]
    public string Agent { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }
}