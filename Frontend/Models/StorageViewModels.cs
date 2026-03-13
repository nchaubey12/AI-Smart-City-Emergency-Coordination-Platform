using System.Text.Json.Serialization;

namespace EmergencyPlatform.Frontend.Models;

// ── Auth ──────────────────────────────────────────────────────────────────────

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
    [JsonPropertyName("success")]  public bool   Success { get; set; }
    [JsonPropertyName("message")]  public string Message { get; set; } = string.Empty;
    [JsonPropertyName("userId")]   public string? UserId { get; set; }
    [JsonPropertyName("name")]     public string? Name   { get; set; }
    [JsonPropertyName("email")]    public string? Email  { get; set; }
    [JsonPropertyName("role")]     public string? Role   { get; set; }
    [JsonPropertyName("token")]    public string? Token  { get; set; }
}

public class UserSession
{
    public string UserId { get; set; } = string.Empty;
    public string Name   { get; set; } = string.Empty;
    public string Email  { get; set; } = string.Empty;
    public string Role   { get; set; } = string.Empty;
    public bool IsAdmin  => Role == "Admin";
}

// ── Reports ───────────────────────────────────────────────────────────────────

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
    [JsonPropertyName("success")]   public bool   Success   { get; set; }
    [JsonPropertyName("message")]   public string Message   { get; set; } = string.Empty;
    [JsonPropertyName("reportId")]  public string? ReportId { get; set; }
    [JsonPropertyName("analysis")]  public IncidentReportViewModel? Analysis { get; set; }
}

// ── Admin Dashboard ───────────────────────────────────────────────────────────

public class AggregatedIncidentSummary
{
    [JsonPropertyName("incidentKey")]          public string IncidentKey       { get; set; } = string.Empty;
    [JsonPropertyName("incidentType")]         public string IncidentType      { get; set; } = string.Empty;
    [JsonPropertyName("severityLevel")]        public string SeverityLevel     { get; set; } = string.Empty;
    [JsonPropertyName("adminSeverity")]        public string? AdminSeverity    { get; set; }
    [JsonPropertyName("effectiveSeverity")]    public string EffectiveSeverity { get; set; } = string.Empty;
    [JsonPropertyName("location")]             public string Location          { get; set; } = string.Empty;
    [JsonPropertyName("lat")]                  public double? Lat              { get; set; }
    [JsonPropertyName("lon")]                  public double? Lon              { get; set; }
    [JsonPropertyName("firstReportedAt")]      public DateTime FirstReportedAt { get; set; }
    [JsonPropertyName("lastReportedAt")]       public DateTime LastReportedAt  { get; set; }
    [JsonPropertyName("reportCount")]          public int ReportCount          { get; set; }
    [JsonPropertyName("dispatchUnits")]        public List<string> DispatchUnits { get; set; } = new();
    [JsonPropertyName("adminDispatchUnits")]   public List<string> AdminDispatchUnits { get; set; } = new();
    [JsonPropertyName("effectiveDispatchUnits")] public List<string> EffectiveDispatchUnits { get; set; } = new();
    [JsonPropertyName("priority")]             public int Priority             { get; set; }
    [JsonPropertyName("status")]               public string Status            { get; set; } = "open";
    [JsonPropertyName("acceptedAt")]           public DateTime? AcceptedAt    { get; set; }
    [JsonPropertyName("acceptedBy")]           public string? AcceptedBy      { get; set; }
    [JsonPropertyName("resolvedAt")]           public DateTime? ResolvedAt    { get; set; }
    [JsonPropertyName("resolvedBy")]           public string? ResolvedBy      { get; set; }
    [JsonPropertyName("adminNotes")]           public string AdminNotes        { get; set; } = string.Empty;
    [JsonPropertyName("dispatchTriggeredAt")]  public DateTime? DispatchTriggeredAt { get; set; }
    [JsonPropertyName("description")]          public string Description       { get; set; } = string.Empty;
    [JsonPropertyName("analysisSummary")]      public string AnalysisSummary   { get; set; } = string.Empty;

    // ── Computed display helpers ──────────────────────────────────────────────

    public string DisplaySeverity => !string.IsNullOrEmpty(AdminSeverity) ? AdminSeverity : SeverityLevel;

    public string SeverityColor => DisplaySeverity switch
    {
        "critical" => "#ff2d55",
        "high"     => "#ff9500",
        "medium"   => "#ffd60a",
        "low"      => "#30d158",
        _          => "#636366"
    };

    public string SeverityColor20 => DisplaySeverity switch
    {
        "critical" => "rgba(255,45,85,0.12)",
        "high"     => "rgba(255,149,0,0.12)",
        "medium"   => "rgba(255,214,10,0.12)",
        "low"      => "rgba(48,209,88,0.12)",
        _          => "rgba(99,99,102,0.12)"
    };

    public string IncidentIcon => IncidentType switch
    {
        "fire"     => "🔥",
        "accident" => "🚗",
        "medical"  => "🚑",
        "crime"    => "🚔",
        "hazard"   => "⚠️",
        _          => "❓"
    };

    public string StatusColor => Status switch
    {
        "open"     => "#ff9500",
        "accepted" => "#0a84ff",
        "resolved" => "#30d158",
        _          => "#636366"
    };

    public string StatusIcon => Status switch
    {
        "open"     => "🟠",
        "accepted" => "🔵",
        "resolved" => "🟢",
        _          => "⚪"
    };
}

// ── Individual Report (drawer) ────────────────────────────────────────────────

public class IndividualReport
{
    [JsonPropertyName("reportId")]    public string ReportId    { get; set; } = string.Empty;
    [JsonPropertyName("userName")]    public string UserName    { get; set; } = string.Empty;
    [JsonPropertyName("userEmail")]   public string UserEmail   { get; set; } = string.Empty;
    [JsonPropertyName("userRole")]    public string UserRole    { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("location")]    public string Location    { get; set; } = string.Empty;
    [JsonPropertyName("submittedAt")] public DateTime SubmittedAt { get; set; }
    [JsonPropertyName("inputType")]   public string InputType   { get; set; } = string.Empty;
}

public class AggregatedIncidentDetail
{
    [JsonPropertyName("incidentKey")]          public string IncidentKey   { get; set; } = string.Empty;
    [JsonPropertyName("incidentType")]         public string IncidentType  { get; set; } = string.Empty;
    [JsonPropertyName("severityLevel")]        public string SeverityLevel { get; set; } = string.Empty;
    [JsonPropertyName("adminSeverity")]        public string? AdminSeverity { get; set; }
    [JsonPropertyName("location")]             public string Location      { get; set; } = string.Empty;
    [JsonPropertyName("reportCount")]          public int ReportCount      { get; set; }
    [JsonPropertyName("dispatchUnits")]        public List<string> DispatchUnits { get; set; } = new();
    [JsonPropertyName("adminDispatchUnits")]   public List<string> AdminDispatchUnits { get; set; } = new();
    [JsonPropertyName("effectiveDispatchUnits")] public List<string> EffectiveDispatchUnits { get; set; } = new();
    [JsonPropertyName("priority")]             public int Priority         { get; set; }
    [JsonPropertyName("status")]               public string Status        { get; set; } = "open";
    [JsonPropertyName("adminNotes")]           public string AdminNotes    { get; set; } = string.Empty;
    [JsonPropertyName("acceptedBy")]           public string? AcceptedBy  { get; set; }
    [JsonPropertyName("resolvedBy")]           public string? ResolvedBy  { get; set; }
    [JsonPropertyName("firstReportedAt")]      public DateTime FirstReportedAt { get; set; }
    [JsonPropertyName("lastReportedAt")]       public DateTime LastReportedAt  { get; set; }
    [JsonPropertyName("reports")]              public List<IndividualReport> Reports { get; set; } = new();

    public string IncidentIcon => IncidentType switch
    {
        "fire"     => "🔥",
        "accident" => "🚗",
        "medical"  => "🚑",
        "crime"    => "🚔",
        "hazard"   => "⚠️",
        _          => "❓"
    };
}

// ── Admin Action DTOs ─────────────────────────────────────────────────────────

public class UpdateIncidentRequest
{
    public string? AdminSeverity          { get; set; }
    public List<string>? AdminDispatchUnits { get; set; }
    public string? AdminNotes             { get; set; }
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