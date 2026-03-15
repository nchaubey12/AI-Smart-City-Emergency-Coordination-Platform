using System.Text.Json.Serialization;

namespace EmergencyPlatform.Frontend.Models;

// ── Auth ──────────────────────────────────────────────────────────────────────

public class RegisterRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public string AdminKey { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    [JsonPropertyName("success")] public bool   Success { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("userId")]  public string? UserId { get; set; }
    [JsonPropertyName("name")]    public string? Name   { get; set; }
    [JsonPropertyName("email")]   public string? Email  { get; set; }
    [JsonPropertyName("role")]    public string? Role   { get; set; }
    [JsonPropertyName("token")]   public string? Token  { get; set; }
}

public class UserSession
{
    public string UserId { get; set; } = string.Empty;
    public string Name   { get; set; } = string.Empty;
    public string Email  { get; set; } = string.Empty;
    public string Role   { get; set; } = string.Empty;
    public bool IsAdmin  => Role == "Admin";
}

// ── Report submission ─────────────────────────────────────────────────────────

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
    [JsonPropertyName("success")]  public bool   Success   { get; set; }
    [JsonPropertyName("message")]  public string Message   { get; set; } = string.Empty;
    [JsonPropertyName("reportId")] public string? ReportId { get; set; }
    [JsonPropertyName("analysis")] public IncidentReportViewModel? Analysis { get; set; }
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

public class AggregatedIncidentSummary
{
    [JsonPropertyName("incidentKey")]         public string IncidentKey          { get; set; } = string.Empty;
    [JsonPropertyName("incidentType")]        public string IncidentType         { get; set; } = string.Empty;
    [JsonPropertyName("adminIncidentType")]   public string? AdminIncidentType   { get; set; }
    [JsonPropertyName("effectiveIncidentType")] public string EffectiveIncidentType { get; set; } = string.Empty;
    [JsonPropertyName("severityLevel")]       public string SeverityLevel        { get; set; } = string.Empty;
    [JsonPropertyName("adminSeverity")]       public string? AdminSeverity       { get; set; }
    [JsonPropertyName("effectiveSeverity")]   public string EffectiveSeverity    { get; set; } = string.Empty;
    [JsonPropertyName("location")]            public string Location             { get; set; } = string.Empty;
    [JsonPropertyName("lat")]                 public double? Lat                 { get; set; }
    [JsonPropertyName("lon")]                 public double? Lon                 { get; set; }
    [JsonPropertyName("firstReportedAt")]     public DateTime FirstReportedAt   { get; set; }
    [JsonPropertyName("lastReportedAt")]      public DateTime LastReportedAt    { get; set; }
    [JsonPropertyName("reportCount")]         public int ReportCount            { get; set; }
    [JsonPropertyName("dispatchUnits")]       public List<string> DispatchUnits { get; set; } = new();
    [JsonPropertyName("status")]              public string Status              { get; set; } = "open";
    [JsonPropertyName("acceptedAt")]          public DateTime? AcceptedAt       { get; set; }
    [JsonPropertyName("acceptedBy")]          public string? AcceptedBy         { get; set; }
    [JsonPropertyName("resolvedAt")]          public DateTime? ResolvedAt       { get; set; }
    [JsonPropertyName("resolvedBy")]          public string? ResolvedBy         { get; set; }
    [JsonPropertyName("adminNotes")]          public string AdminNotes          { get; set; } = string.Empty;
    [JsonPropertyName("dispatchTriggeredAt")] public DateTime? DispatchTriggeredAt { get; set; }
    [JsonPropertyName("description")]         public string Description         { get; set; } = string.Empty;
    [JsonPropertyName("analysisSummary")]     public string AnalysisSummary     { get; set; } = string.Empty;

    // ── Computed helpers ──────────────────────────────────────────────────────

    public string DisplaySeverity     => !string.IsNullOrEmpty(AdminSeverity)      ? AdminSeverity      : SeverityLevel;
    public string DisplayIncidentType => !string.IsNullOrEmpty(AdminIncidentType)  ? AdminIncidentType  : IncidentType;

    public string SeverityColor => DisplaySeverity switch
    {
        "critical" => "#991b1b", "high" => "#92400e",
        "medium"   => "#713f12", "low"  => "#14532d", _ => "#374151"
    };
    public string SeverityBg => DisplaySeverity switch
    {
        "critical" => "#fee2e2", "high" => "#ffedd5",
        "medium"   => "#fef9c3", "low"  => "#dcfce7", _ => "#f3f4f6"
    };

    public string IncidentIcon => DisplayIncidentType switch
    {
        "fire"           => "🔥", "accident"       => "🚗",
        "medical"        => "🚑", "crime"          => "🚔",
        "hazard"         => "⚠️", "flood"          => "🌊",
        "earthquake"     => "🏚️", "riot"           => "👥",
        "explosion"      => "💥", "missing_person" => "🔍",
        _ => "📋"
    };

    public string StatusColor => Status switch
    {
        "open"     => "#92400e", "accepted" => "#1e40af", "resolved" => "#065f46", _ => "#374151"
    };
    public string StatusBg => Status switch
    {
        "open"     => "#fef3c7", "accepted" => "#dbeafe", "resolved" => "#d1fae5", _ => "#f3f4f6"
    };
    public string StatusLabel => Status switch
    {
        "open" => "Pending Review", "accepted" => "Dispatched", "resolved" => "Resolved", _ => Status
    };
}

// ── Incident detail (drawer) ──────────────────────────────────────────────────

public class IndividualReport
{
    [JsonPropertyName("reportId")]       public string ReportId       { get; set; } = string.Empty;
    [JsonPropertyName("userName")]       public string UserName       { get; set; } = string.Empty;
    [JsonPropertyName("userEmail")]      public string UserEmail      { get; set; } = string.Empty;
    [JsonPropertyName("userRole")]       public string UserRole       { get; set; } = string.Empty;
    [JsonPropertyName("description")]    public string Description    { get; set; } = string.Empty;
    [JsonPropertyName("location")]       public string Location       { get; set; } = string.Empty;
    [JsonPropertyName("submittedAt")]    public DateTime SubmittedAt  { get; set; }
    [JsonPropertyName("inputType")]      public string InputType      { get; set; } = string.Empty;
    [JsonPropertyName("isDuplicate")]    public bool IsDuplicate      { get; set; }
    [JsonPropertyName("duplicateOfKey")] public string? DuplicateOfKey { get; set; }
    [JsonPropertyName("uploadedImage")]  public string? UploadedImage { get; set; }
    [JsonPropertyName("uploadedAudio")]  public string? UploadedAudio { get; set; }
    [JsonPropertyName("uploadedMimeType")] public string? UploadedMimeType { get; set; }
}

public class AggregatedIncidentDetail
{
    [JsonPropertyName("incidentKey")]       public string IncidentKey    { get; set; } = string.Empty;
    [JsonPropertyName("incidentType")]      public string IncidentType   { get; set; } = string.Empty;
    [JsonPropertyName("adminIncidentType")] public string? AdminIncidentType { get; set; }
    [JsonPropertyName("severityLevel")]     public string SeverityLevel  { get; set; } = string.Empty;
    [JsonPropertyName("adminSeverity")]     public string? AdminSeverity { get; set; }
    [JsonPropertyName("location")]          public string Location       { get; set; } = string.Empty;
    [JsonPropertyName("reportCount")]       public int ReportCount       { get; set; }
    [JsonPropertyName("dispatchUnits")]     public List<string> DispatchUnits { get; set; } = new();
    [JsonPropertyName("status")]            public string Status         { get; set; } = "open";
    [JsonPropertyName("adminNotes")]        public string AdminNotes     { get; set; } = string.Empty;
    [JsonPropertyName("acceptedBy")]        public string? AcceptedBy    { get; set; }
    [JsonPropertyName("resolvedBy")]        public string? ResolvedBy    { get; set; }
    [JsonPropertyName("firstReportedAt")]   public DateTime FirstReportedAt { get; set; }
    [JsonPropertyName("lastReportedAt")]    public DateTime LastReportedAt  { get; set; }
    [JsonPropertyName("reports")]           public List<IndividualReport> Reports { get; set; } = new();

    public string IncidentIcon => (AdminIncidentType ?? IncidentType) switch
    {
        "fire"       => "🔥", "accident"       => "🚗",
        "medical"    => "🚑", "crime"          => "🚔",
        "hazard"     => "⚠️", "flood"          => "🌊",
        "earthquake" => "🏚️", "riot"           => "👥",
        "explosion"  => "💥", "missing_person" => "🔍",
        _ => "📋"
    };
}

// ── Admin action DTOs ─────────────────────────────────────────────────────────

public class UpdateIncidentRequest
{
    public string? AdminSeverity       { get; set; }
    public string? AdminIncidentType   { get; set; }
    public List<string>? DispatchUnits { get; set; }
    public string? AdminNotes          { get; set; }
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

public class MarkDuplicateRequest
{
    public string ReportId  { get; set; } = string.Empty;
    public string TargetKey { get; set; } = string.Empty;
    public string AdminName { get; set; } = string.Empty;
}

// Slim model for the duplicate dropdown
public class OpenIncidentOption
{
    [JsonPropertyName("incidentKey")]          public string IncidentKey          { get; set; } = string.Empty;
    [JsonPropertyName("effectiveIncidentType")] public string EffectiveIncidentType { get; set; } = string.Empty;
    [JsonPropertyName("location")]             public string Location             { get; set; } = string.Empty;
    [JsonPropertyName("status")]               public string Status               { get; set; } = string.Empty;
}