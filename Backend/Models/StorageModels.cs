using System.Text.Json.Serialization;

namespace EmergencyPlatform.Models;

// ── User Models ───────────────────────────────────────────────────────────────

public class AppUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpper();
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    [JsonPropertyName("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;
    [JsonPropertyName("role")]
    public string Role { get; set; } = "User";
    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;
    [JsonPropertyName("registeredAt")]
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}

public class UserStore
{
    [JsonPropertyName("users")]
    public List<AppUser> Users { get; set; } = new();
}

// ── Report Models ─────────────────────────────────────────────────────────────

public class StoredReport
{
    [JsonPropertyName("reportId")]
    public string ReportId { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpper();
    [JsonPropertyName("incidentKey")]
    public string IncidentKey { get; set; } = string.Empty;
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;
    [JsonPropertyName("userEmail")]
    public string UserEmail { get; set; } = string.Empty;
    [JsonPropertyName("userRole")]
    public string UserRole { get; set; } = string.Empty;
    [JsonPropertyName("inputType")]
    public string InputType { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;
    [JsonPropertyName("lat")]
    public double? Lat { get; set; }
    [JsonPropertyName("lon")]
    public double? Lon { get; set; }
    [JsonPropertyName("submittedAt")]
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("analysisResult")]
    public IncidentReport? AnalysisResult { get; set; }

    // Set when admin marks this as duplicate of another incident
    [JsonPropertyName("isDuplicate")]
    public bool IsDuplicate { get; set; } = false;
    [JsonPropertyName("duplicateOfKey")]
    public string? DuplicateOfKey { get; set; }
}

public class AggregatedIncident
{
    [JsonPropertyName("incidentKey")]
    public string IncidentKey { get; set; } = string.Empty;

    [JsonPropertyName("incidentType")]
    public string IncidentType { get; set; } = string.Empty;

    [JsonPropertyName("adminIncidentType")]
    public string? AdminIncidentType { get; set; }

    [JsonIgnore]
    public string EffectiveIncidentType => AdminIncidentType ?? IncidentType;

    [JsonPropertyName("severityLevel")]
    public string SeverityLevel { get; set; } = string.Empty;

    [JsonPropertyName("adminSeverity")]
    public string? AdminSeverity { get; set; }

    [JsonIgnore]
    public string EffectiveSeverity => AdminSeverity ?? SeverityLevel;

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("lat")]
    public double? Lat { get; set; }

    [JsonPropertyName("lon")]
    public double? Lon { get; set; }

    [JsonPropertyName("firstReportedAt")]
    public DateTime FirstReportedAt { get; set; }

    [JsonPropertyName("lastReportedAt")]
    public DateTime LastReportedAt { get; set; }

    [JsonPropertyName("reportCount")]
    public int ReportCount { get; set; } = 1;

    // All dispatch units (AI + admin added/removed — admin list is the full override)
    [JsonPropertyName("dispatchUnits")]
    public List<string> DispatchUnits { get; set; } = new();

    // open | accepted | resolved
    [JsonPropertyName("status")]
    public string Status { get; set; } = "open";

    [JsonPropertyName("acceptedAt")]
    public DateTime? AcceptedAt { get; set; }

    [JsonPropertyName("acceptedBy")]
    public string? AcceptedBy { get; set; }

    [JsonPropertyName("resolvedAt")]
    public DateTime? ResolvedAt { get; set; }

    [JsonPropertyName("resolvedBy")]
    public string? ResolvedBy { get; set; }

    [JsonPropertyName("adminNotes")]
    public string AdminNotes { get; set; } = string.Empty;

    [JsonPropertyName("dispatchTriggeredAt")]
    public DateTime? DispatchTriggeredAt { get; set; }

    [JsonPropertyName("reports")]
    public List<StoredReport> Reports { get; set; } = new();
}

public class ReportStore
{
    [JsonPropertyName("incidents")]
    public List<AggregatedIncident> Incidents { get; set; } = new();
}

// ── Auth DTOs ─────────────────────────────────────────────────────────────────

public class RegisterRequest
{
    public string Name     { get; set; } = string.Empty;
    public string Email    { get; set; } = string.Empty;
    public string Phone    { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role     { get; set; } = "User";
    public string AdminKey { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string Email    { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    public bool   Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Name   { get; set; }
    public string? Email  { get; set; }
    public string? Role   { get; set; }
    public string? Token  { get; set; }
}

// ── Report DTOs ───────────────────────────────────────────────────────────────

public class SubmitReportRequest
{
    public string UserId      { get; set; } = string.Empty;
    public string UserName    { get; set; } = string.Empty;
    public string UserEmail   { get; set; } = string.Empty;
    public string UserRole    { get; set; } = string.Empty;
    public string InputType   { get; set; } = "text";
    public string Description { get; set; } = string.Empty;
    public string Base64Image { get; set; } = string.Empty;
    public string Base64Audio { get; set; } = string.Empty;
    public string MimeType    { get; set; } = "image/jpeg";
    public double? Lat        { get; set; }
    public double? Lon        { get; set; }
}

public class SubmitReportResponse
{
    public bool   Success    { get; set; }
    public string Message    { get; set; } = string.Empty;
    public string? ReportId  { get; set; }
    public IncidentReport? Analysis { get; set; }
}

// ── Admin Action DTOs ─────────────────────────────────────────────────────────

public class UpdateIncidentRequest
{
    public string? AdminSeverity     { get; set; }
    public string? AdminIncidentType { get; set; }
    public List<string>? DispatchUnits { get; set; }  // full replacement list
    public string? AdminNotes        { get; set; }
}

public class AcceptIncidentRequest
{
    public string AdminName { get; set; } = string.Empty;
    public string? Notes    { get; set; }
}

public class ResolveIncidentRequest
{
    public string AdminName { get; set; } = string.Empty;
    public string? Notes    { get; set; }
}

// Admin marks an orphan report as duplicate of an existing incident
public class MarkDuplicateRequest
{
    public string ReportId      { get; set; } = string.Empty;  // orphan report id
    public string TargetKey     { get; set; } = string.Empty;  // existing incident to merge into
    public string AdminName     { get; set; } = string.Empty;
}