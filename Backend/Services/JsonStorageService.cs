using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EmergencyPlatform.Models;

namespace EmergencyPlatform.Services;

/// <summary>
/// Handles all JSON file persistence for users and incident reports.
/// Files are stored in the /data directory next to the executable.
/// Thread-safe via SemaphoreSlim for concurrent API requests.
/// </summary>
public class JsonStorageService
{
    private readonly string _usersFile;
    private readonly string _reportsFile;
    private readonly ILogger<JsonStorageService> _logger;
    private readonly SemaphoreSlim _userLock   = new(1, 1);
    private readonly SemaphoreSlim _reportLock = new(1, 1);

    private const string AdminSecretKey = "EMERGENCY_ADMIN_2024"; // change in production

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

        // Seed default admin if no users exist
        _ = SeedAdminAsync();
    }

    // ── Password Hashing ──────────────────────────────────────────────────────

    public static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password + "emergency_salt"));
        return Convert.ToHexString(bytes).ToLower();
    }

    // ── User Operations ───────────────────────────────────────────────────────

    public async Task<UserStore> LoadUsersAsync()
    {
        await _userLock.WaitAsync();
        try
        {
            if (!File.Exists(_usersFile))
                return new UserStore();
            var json = await File.ReadAllTextAsync(_usersFile);
            return JsonSerializer.Deserialize<UserStore>(json, JsonOpts) ?? new UserStore();
        }
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

            // Check duplicate email
            if (store.Users.Any(u => u.Email == email))
                return new AuthResponse { Success = false, Message = "This email is already registered." };

            // Check duplicate phone
            if (!string.IsNullOrEmpty(req.Phone) &&
                store.Users.Any(u => u.Phone == req.Phone.Trim()))
                return new AuthResponse { Success = false, Message = "This phone number is already registered." };

            // Admin key check
            var role = "User";
            if (req.Role == "Admin")
            {
                if (req.AdminKey != AdminSecretKey)
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

            _logger.LogInformation("User registered: {Email} as {Role}", email, role);
            return new AuthResponse
            {
                Success = true,
                Message = "Registration successful!",
                UserId  = user.Id,
                Name    = user.Name,
                Email   = user.Email,
                Role    = user.Role
            };
        }
        finally { _userLock.Release(); }
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        var store = await LoadUsersAsync();
        var email = req.Email.Trim().ToLower();
        var hash  = HashPassword(req.Password);

        var user = store.Users.FirstOrDefault(u => u.Email == email && u.PasswordHash == hash);
        if (user == null)
            return new AuthResponse { Success = false, Message = "Invalid email or password." };

        return new AuthResponse
        {
            Success = true,
            Message = "Login successful!",
            UserId  = user.Id,
            Name    = user.Name,
            Email   = user.Email,
            Role    = user.Role,
            Token   = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user.Id}:{user.Email}:{user.Role}"))
        };
    }

    // ── Report Operations ─────────────────────────────────────────────────────

    public async Task<ReportStore> LoadReportsAsync()
    {
        await _reportLock.WaitAsync();
        try
        {
            if (!File.Exists(_reportsFile))
                return new ReportStore();
            var json = await File.ReadAllTextAsync(_reportsFile);
            return JsonSerializer.Deserialize<ReportStore>(json, JsonOpts) ?? new ReportStore();
        }
        finally { _reportLock.Release(); }
    }

    public async Task<string> SaveReportAsync(StoredReport report, IncidentReport analysis)
    {
        await _reportLock.WaitAsync();
        try
        {
            var store = await LoadReportsInternalAsync();

            report.AnalysisResult = analysis;

            // Build incident key for deduplication:
            // Same incident_type + similar location = same incident
            var incidentKey = BuildIncidentKey(analysis.IncidentType,
                report.Location, report.Lat, report.Lon);
            report.IncidentKey = incidentKey;

            // Find existing aggregated incident
            var existing = store.Incidents.FirstOrDefault(i => i.IncidentKey == incidentKey);

            if (existing != null)
            {
                // Merge: increment count, update last reported time
                existing.ReportCount++;
                existing.LastReportedAt = DateTime.UtcNow;
                existing.Reports.Add(report);

                // Escalate severity if newer report is worse
                if (SeverityRank(analysis.SeverityLevel) > SeverityRank(existing.SeverityLevel))
                    existing.SeverityLevel = analysis.SeverityLevel;
            }
            else
            {
                // New incident
                var incident = new AggregatedIncident
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
                    Reports         = new List<StoredReport> { report }
                };
                store.Incidents.Add(incident);
            }

            var json = JsonSerializer.Serialize(store, JsonOpts);
            await File.WriteAllTextAsync(_reportsFile, json);

            _logger.LogInformation("Report {Id} saved, incident key: {Key}",
                report.ReportId, incidentKey);
            return report.ReportId;
        }
        finally { _reportLock.Release(); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildIncidentKey(string incidentType, string location,
        double? lat, double? lon)
    {
        // If we have GPS, round to ~100m grid squares for deduplication
        if (lat.HasValue && lon.HasValue)
        {
            var latGrid = Math.Round(lat.Value, 3);
            var lonGrid = Math.Round(lon.Value, 3);
            return $"{incidentType}:{latGrid}:{lonGrid}".ToLower();
        }

        // Fallback: normalize location text
        var locKey = location.ToLower().Trim()
            .Replace(" ", "").Replace(",", "").Replace(".", "");
        return $"{incidentType}:{locKey}".ToLower();
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "critical" => 4,
        "high"     => 3,
        "medium"   => 2,
        "low"      => 1,
        _          => 0
    };

    private async Task SeedAdminAsync()
    {
        var store = await LoadUsersAsync();
        if (store.Users.Any()) return;

        // Seed a default admin account
        await _userLock.WaitAsync();
        try
        {
            var s = await LoadUsersInternalAsync();
            if (s.Users.Any()) return;

            s.Users.Add(new AppUser
            {
                Name         = "Admin",
                Email        = "admin@emergency.ai",
                Phone        = "0000000000",
                PasswordHash = HashPassword("Admin@123"),
                Role         = "Admin"
            });
            await SaveUsersAsync(s);
            _logger.LogInformation("Default admin seeded: admin@emergency.ai / Admin@123");
        }
        finally { _userLock.Release(); }
    }

    // Internal versions (no lock — caller holds lock)
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
