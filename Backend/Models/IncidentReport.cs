using System.Text.Json.Serialization;

namespace EmergencyPlatform.Models;

// ─── Enumerations ──────────────────────────────────────────────────────────────

public enum InputType   { Text, Image, Audio }
public enum IncidentType { Fire, Accident, Medical, Crime, Hazard, Unknown }
public enum SeverityLevel { Low, Medium, High, Critical }

// ─── Core Incident Report (matches JSON schema in spec) ───────────────────────

public class IncidentReport
{
    [JsonPropertyName("input_type")]
    public string InputType { get; set; } = "text";

    [JsonPropertyName("incident_type")]
    public string IncidentType { get; set; } = "unknown";

    [JsonPropertyName("severity_level")]
    public string SeverityLevel { get; set; } = "low";

    [JsonPropertyName("location_data")]
    public LocationData LocationData { get; set; } = new();

    [JsonPropertyName("entities")]
    public EntityData Entities { get; set; } = new();

    [JsonPropertyName("reasoning_summary")]
    public string ReasoningSummary { get; set; } = string.Empty;

    [JsonPropertyName("dispatch_recommendation")]
    public DispatchRecommendation DispatchRecommendation { get; set; } = new();

    // Extra metadata for UI
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("incident_id")]
    public string IncidentId { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpper();

    [JsonPropertyName("agent_trace")]
    public List<AgentStep> AgentTrace { get; set; } = new();
}

public class LocationData
{
    [JsonPropertyName("lat")]
    public double? Lat { get; set; }

    [JsonPropertyName("lon")]
    public double? Lon { get; set; }

    [JsonPropertyName("inferred_location_description")]
    public string InferredLocationDescription { get; set; } = string.Empty;
}

public class EntityData
{
    [JsonPropertyName("people_involved")]
    public int PeopleInvolved { get; set; }

    [JsonPropertyName("vehicles_involved")]
    public int VehiclesInvolved { get; set; }

    [JsonPropertyName("hazards")]
    public string Hazards { get; set; } = string.Empty;
}

public class DispatchRecommendation
{
    [JsonPropertyName("units_required")]
    public List<string> UnitsRequired { get; set; } = new();

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 3;
}

// ─── Agent Tracing ─────────────────────────────────────────────────────────────

public class AgentStep
{
    [JsonPropertyName("agent")]
    public string Agent { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }
}

// ─── API Request Models ────────────────────────────────────────────────────────

public class TextReportRequest
{
    public string Text { get; set; } = string.Empty;
    public double? Lat { get; set; }
    public double? Lon { get; set; }
}

public class ImageReportRequest
{
    public string Base64Image { get; set; } = string.Empty;     // base64 encoded
    public string MimeType   { get; set; } = "image/jpeg";
    public double? Lat { get; set; }
    public double? Lon { get; set; }
}

public class AudioReportRequest
{
    public string Base64Audio { get; set; } = string.Empty;
    public string MimeType   { get; set; } = "audio/wav";
    public double? Lat { get; set; }
    public double? Lon { get; set; }
}
