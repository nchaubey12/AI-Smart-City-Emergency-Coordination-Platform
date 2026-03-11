using Microsoft.AspNetCore.Mvc;
using EmergencyPlatform.Models;
using EmergencyPlatform.Services;

namespace EmergencyPlatform.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly JsonStorageService _storage;

    public AuthController(JsonStorageService storage) => _storage = storage;

    /// <summary>POST /api/auth/register</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) ||
            string.IsNullOrWhiteSpace(req.Email) ||
            string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new AuthResponse { Success = false, Message = "All fields are required." });

        if (req.Password.Length < 6)
            return BadRequest(new AuthResponse { Success = false, Message = "Password must be at least 6 characters." });

        var result = await _storage.RegisterAsync(req);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>POST /api/auth/login</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new AuthResponse { Success = false, Message = "Email and password required." });

        var result = await _storage.LoginAsync(req);
        return result.Success ? Ok(result) : Unauthorized(result);
    }

    /// <summary>GET /api/auth/users — Admin only, for debugging</summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var store = await _storage.LoadUsersAsync();
        // Never return password hashes
        var safe = store.Users.Select(u => new
        {
            u.Id, u.Name, u.Email, u.Phone, u.Role, u.RegisteredAt
        });
        return Ok(safe);
    }
}
