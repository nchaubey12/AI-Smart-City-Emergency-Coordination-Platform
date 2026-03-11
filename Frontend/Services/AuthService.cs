using System.Text.Json;

namespace EmergencyPlatform.Frontend.Services;

/// <summary>
/// Simple client-side auth service.
/// In production this would call a real auth API.
/// For demo: one allowed user hardcoded, stored in memory.
/// </summary>
public class AuthService
{
    // ── The ONE allowed user ──────────────────────────────────────────────
    private const string AllowedEmail  = "admin@emergency.ai";
    private const string AllowedPhone  = "9999999999";
    private const string AllowedPassword = "Admin@123";

    // Track if the allowed user is already registered
    private static bool _isRegistered = false;
    private static string? _registeredEmail;
    private static string? _registeredPhone;

    // Current session
    public bool IsLoggedIn { get; private set; } = false;
    public string? CurrentUserEmail { get; private set; }

    public event Action? OnAuthStateChanged;

    // ── Register ──────────────────────────────────────────────────────────

    public (bool Success, string Message) Register(string email, string phone, string password)
    {
        email = email.Trim().ToLower();
        phone = phone.Trim();

        // Check if email or phone already registered
        if (_isRegistered)
        {
            if (_registeredEmail == email)
                return (false, "This email is already registered.");
            if (_registeredPhone == phone)
                return (false, "This phone number is already registered.");
            return (false, "Registration is closed. Only one account is allowed.");
        }

        // Validate against allowed credentials
        if (email != AllowedEmail)
            return (false, "This email is not authorized to register.");
        if (phone != AllowedPhone)
            return (false, "This phone number is not authorized to register.");
        if (password.Length < 6)
            return (false, "Password must be at least 6 characters.");

        // Register
        _isRegistered    = true;
        _registeredEmail = email;
        _registeredPhone = phone;

        return (true, "Registration successful! You can now log in.");
    }

    // ── Login ─────────────────────────────────────────────────────────────

    public (bool Success, string Message) Login(string email, string password)
    {
        email = email.Trim().ToLower();

        if (!_isRegistered)
            return (false, "No account found. Please register first.");
        if (_registeredEmail != email)
            return (false, "Invalid email or password.");
        if (password != AllowedPassword)
            return (false, "Invalid email or password.");

        IsLoggedIn       = true;
        CurrentUserEmail = email;
        OnAuthStateChanged?.Invoke();
        return (true, "Login successful!");
    }

    // ── Logout ────────────────────────────────────────────────────────────

    public void Logout()
    {
        IsLoggedIn       = false;
        CurrentUserEmail = null;
        OnAuthStateChanged?.Invoke();
    }
}
