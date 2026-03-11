using System.Net.Http.Json;
using System.Text.Json;
using EmergencyPlatform.Frontend.Models;

namespace EmergencyPlatform.Frontend.Services;

/// <summary>
/// Handles authentication against the backend API.
/// Session is kept in memory (Blazor WASM singleton).
/// On page refresh, user must log in again — acceptable for this stage.
/// </summary>
public class AuthService
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    public UserSession? CurrentUser { get; private set; }
    public bool IsLoggedIn => CurrentUser != null;
    public bool IsAdmin    => CurrentUser?.IsAdmin ?? false;

    public event Action? OnAuthChanged;

    public AuthService(HttpClient http) => _http = http;

    // ── Register ──────────────────────────────────────────────────────────────

    public async Task<AuthResponse> RegisterAsync(RegisterRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/auth/register", req);
            var body = await resp.Content.ReadFromJsonAsync<AuthResponse>(_opts)
                       ?? new AuthResponse { Success = false, Message = "No response from server." };
            return body;
        }
        catch (Exception ex)
        {
            return new AuthResponse { Success = false, Message = $"Connection error: {ex.Message}" };
        }
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/auth/login", req);
            var body = await resp.Content.ReadFromJsonAsync<AuthResponse>(_opts)
                       ?? new AuthResponse { Success = false, Message = "No response from server." };

            if (body.Success && body.UserId != null)
            {
                CurrentUser = new UserSession
                {
                    UserId = body.UserId,
                    Name   = body.Name   ?? string.Empty,
                    Email  = body.Email  ?? string.Empty,
                    Role   = body.Role   ?? "User"
                };
                OnAuthChanged?.Invoke();
            }

            return body;
        }
        catch (Exception ex)
        {
            return new AuthResponse { Success = false, Message = $"Connection error: {ex.Message}" };
        }
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    public void Logout()
    {
        CurrentUser = null;
        OnAuthChanged?.Invoke();
    }
}
