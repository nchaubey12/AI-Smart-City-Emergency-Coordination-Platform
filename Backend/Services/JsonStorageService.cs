using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EmergencyPlatform.Models;

namespace EmergencyPlatform.Services;

public class JsonStorageService
{
    private readonly string _usersFile;
    private readonly string _reportsFile;
    private readonly ILogger<JsonStorageService> _logger;
    private readonly SemaphoreSlim _userLock   = new(1, 1);
    private readonly SemaphoreSlim _reportLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonStorageService(IConfiguration config, ILogger<JsonStorageService> logger)
    {
        _logger = logger;
        var dataDir = config["DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _usersFile   = Path.Combine(dataDir, "users.json");
        _reportsFile = Path.Combine(dataDir, "reports.json");
        _ = SeedAdminAsync();
    }

    // ── Password ──────────────────────────────────────────────────────────────

    public static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password + "emergency_salt"));
        return Convert.ToHexString(bytes).ToLower();
    }

    // ── User Operations ───────────────────────────────────────────────────────

    public async Task<UserStore> LoadUsersAsync()
    {
        await _userLock.WaitAsync();
        try   { return await LoadUsersInternalAsync(); }
        finally { _userLock.Release(); }
    }

    private async Task SaveUsersAsync(UserStore store)
    {
        var json = JsonSerializer.Serialize(store, JsonOpts);
        await File.WriteAllTextAsync(_usersFile, json);
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest req)
    {
        await _userLock.WaitAsync();
        try
        {
            var store = await LoadUsersInternalAsync();
            var email = req.Email.Trim().ToLower();

            if (store.Users.Any(u => u.Email == email))
                return new AuthResponse { Success = false, Message = "This email is already registered." };

            if (!string.IsNullOrEmpty(req.Phone) && store.Users.Any(u => u.Phone == req.Phone.Trim()))
                return new AuthResponse { Success = false, Message = "This phone number is already registered." };

            var role = "User";
            if (req.Role == "Admin")
            {
                if (req.AdminKey != "EMERGENCY_ADMIN_2024")
                    return new AuthResponse { Success = false, Message = "Invalid admin key." };
                role = "Admin";
            }

            var user = new AppUser
            {
                Name         = req.Name.Trim(),
                Email        = email,
                Phone        = req.Phone.Trim(),
                PasswordHash = HashPassword(req.Password),
                Role         = role
            };

            store.Users.Add(user);
            await SaveUsersAsync(store);

            return new AuthResponse
            {
                Success = true, Message = "Registration successful!",
                UserId = user.Id, Name = user.Name, Email = user.Email, Role = user.Role
            };
        }
        finally { _userLock.Release(); }
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        var store = await LoadUsersAsync();
        var email = req.Email.Trim().ToLower();
        var hash  = HashPassword(req.Password);
        var user  = store.Users.FirstOrDefault(u => u.Email == email && u.PasswordHash == hash);

        if (user == null)
            return new AuthResponse { Success = false, Message = "Invalid email or password." };

        return new AuthResponse
        {
            Success = true, Message = "Login successful!",
            UserId = user.Id, Name = user.Name, Email = user.Email, Role = user.Role,
            Token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user.Id}:{user.Email}:{user.Role}"))
        };
    }

    // ── Report Operations ─────────────────────────────────────────────────────

    public async Task<ReportStore> LoadReportsAsync()
    {
        await _reportLock.WaitAsync();
        try   { return await LoadReportsInternalAsync(); }
        finally { _reportLock.Release(); }
    }

    public async Task<string> SaveReportAsync(StoredReport report, IncidentReport analysis)
    {
        await _reportLock.WaitAsync();
        try
        {
            var store = await LoadReportsInternalAsync();
            report.AnalysisResult = analysis;

            var incidentKey = BuildIncidentKey(analysis.IncidentType,
                report.Location, report.Lat, report.Lon);
            report.IncidentKey = incidentKey;

            var existing = store.Incidents.FirstOrDefault(i => i.IncidentKey == incidentKey);

            if (existing != null)
            {
                existing.ReportCount++;
                existing.LastReportedAt = DateTime.UtcNow;
                existing.Reports.Add(report);

                if (SeverityRank(analysis.SeverityLevel) > SeverityRank(existing.SeverityLevel))
                    existing.SeverityLevel = analysis.SeverityLevel;
            }
            else
            {
                store.Incidents.Add(new AggregatedIncident
                {
                    IncidentKey     = incidentKey,
                    IncidentType    = analysis.IncidentType,
                    SeverityLevel   = analysis.SeverityLevel,
                    Location        = report.Location,
                    Lat             = report.Lat,
                    Lon             = report.Lon,
                    FirstReportedAt = DateTime.UtcNow,
                    LastReportedAt  = DateTime.UtcNow,
                    ReportCount     = 1,
                    DispatchUnits   = analysis.DispatchRecommendation.UnitsRequired,
                    Priority        = analysis.DispatchRecommendation.Priority,
                    Status          = "open",
                    Reports         = new List<StoredReport> { report }
                });
            }

            await File.WriteAllTextAsync(_reportsFile, JsonSerializer.Serialize(store, JsonOpts));
            return report.ReportId;
        }
        finally { _reportLock.Release(); }
    }

    // ── Admin: Update severity / dispatch units / notes ───────────────────────

    public async Task<bool> UpdateIncidentAsync(string key, UpdateIncidentRequest req)
    {
        await _reportLock.WaitAsync();
        try
        {
            var store    = await LoadReportsInternalAsync();
            var incident = store.Incidents.FirstOrDefault(i => i.IncidentKey == key);
            if (incident == null) return false;

            if (!string.IsNullOrWhiteSpace(req.AdminSeverity))
                incident.AdminSeverity = req.AdminSeverity;

            if (req.AdminDispatchUnits != null)
                incident.AdminDispatchUnits = req.AdminDispatchUnits;

            if (req.AdminNotes != null)
                incident.AdminNotes = req.AdminNotes;

            await File.WriteAllTextAsync(_reportsFile, JsonSerializer.Serialize(store, JsonOpts));
            return true;
        }
        finally { _reportLock.Release(); }
    }

    // ── Admin: Accept incident — triggers dispatch ────────────────────────────

    public async Task<bool> AcceptIncidentAsync(string key, AcceptIncidentRequest req)
    {
        await _reportLock.WaitAsync();
        try
        {
            var store    = await LoadReportsInternalAsync();
            var incident = store.Incidents.FirstOrDefault(i => i.IncidentKey == key);
            if (incident == null) return false;

            incident.Status              = "accepted";
            incident.AcceptedAt          = DateTime.UtcNow;
            incident.AcceptedBy          = req.AdminName;
            incident.DispatchTriggeredAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(req.Notes))
                incident.AdminNotes = req.Notes;

            await File.WriteAllTextAsync(_reportsFile, JsonSerializer.Serialize(store, JsonOpts));
            return true;
        }
        finally { _reportLock.Release(); }
    }

    // ── Admin: Resolve incident ───────────────────────────────────────────────

    public async Task<bool> ResolveIncidentAsync(string key, ResolveIncidentRequest req)
    {
        await _reportLock.WaitAsync();
        try
        {
            var store    = await LoadReportsInternalAsync();
            var incident = store.Incidents.FirstOrDefault(i => i.IncidentKey == key);
            if (incident == null) return false;

            incident.Status     = "resolved";
            incident.ResolvedAt = DateTime.UtcNow;
            incident.ResolvedBy = req.AdminName;
            if (!string.IsNullOrWhiteSpace(req.Notes))
                incident.AdminNotes = req.Notes;

            await File.WriteAllTextAsync(_reportsFile, JsonSerializer.Serialize(store, JsonOpts));
            return true;
        }
        finally { _reportLock.Release(); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildIncidentKey(string incidentType, string location,
        double? lat, double? lon)
    {
        if (lat.HasValue && lon.HasValue)
        {
            var latGrid = Math.Round(lat.Value, 3);
            var lonGrid = Math.Round(lon.Value, 3);
            return $"{incidentType}:{latGrid}:{lonGrid}".ToLower();
        }
        var locKey = location.ToLower().Trim()
            .Replace(" ", "").Replace(",", "").Replace(".", "");
        return $"{incidentType}:{locKey}".ToLower();
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "critical" => 4, "high" => 3, "medium" => 2, "low" => 1, _ => 0
    };

    private async Task SeedAdminAsync()
    {
        var store = await LoadUsersAsync();
        if (store.Users.Any()) return;

        await _userLock.WaitAsync();
        try
        {
            var s = await LoadUsersInternalAsync();
            if (s.Users.Any()) return;
            s.Users.Add(new AppUser
            {
                Name = "Admin", Email = "admin@emergency.ai", Phone = "0000000000",
                PasswordHash = HashPassword("Admin@123"), Role = "Admin"
            });
            await SaveUsersAsync(s);
        }
        finally { _userLock.Release(); }
    }

    private async Task<UserStore> LoadUsersInternalAsync()
    {
        if (!File.Exists(_usersFile)) return new UserStore();
        var json = await File.ReadAllTextAsync(_usersFile);
        return JsonSerializer.Deserialize<UserStore>(json, JsonOpts) ?? new UserStore();
    }

    private async Task<ReportStore> LoadReportsInternalAsync()
    {
        if (!File.Exists(_reportsFile)) return new ReportStore();
        var json = await File.ReadAllTextAsync(_reportsFile);
        return JsonSerializer.Deserialize<ReportStore>(json, JsonOpts) ?? new ReportStore();
    }
}