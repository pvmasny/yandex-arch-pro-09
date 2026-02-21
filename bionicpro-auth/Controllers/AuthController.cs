using BionicProAuth.Models;
using BionicProAuth.Services;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;

namespace BionicProAuth.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly KeycloakService _keycloakService;
    private readonly SessionService _sessionService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        KeycloakService keycloakService,
        SessionService sessionService,
        ILogger<AuthController> logger)
    {
        _keycloakService = keycloakService;
        _sessionService = sessionService;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var tokenResponse = await _keycloakService.AuthenticateAsync(request.Username, request.Password);

            if (tokenResponse == null)
            {
                return Unauthorized(new { error = "Invalid credentials" });
            }

            // Извлекаем информацию о пользователе из токена
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(tokenResponse.AccessToken);
            var userId = jwtToken.Subject ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value ?? "unknown";
            var username = jwtToken.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value ?? request.Username;

            // Создаем сессию
            var sessionId = await _sessionService.CreateSessionAsync(tokenResponse, username, userId);

            // Устанавливаем session cookie
            Response.Cookies.Append(BionicProSessionOptions.CookieName, sessionId, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                MaxAge = TimeSpan.FromMinutes(30)
            });

            _logger.LogInformation("User {Username} logged in successfully", username);

            return Ok(new
            {
                message = "Login successful",
                user = new
                {
                    id = userId,
                    username = username
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            var sessionId = Request.Cookies[BionicProSessionOptions.CookieName];

            if (!string.IsNullOrEmpty(sessionId))
            {
                var session = await _sessionService.GetSessionAsync(sessionId);
                if (session != null)
                {
                    await _keycloakService.RevokeTokenAsync(session.RefreshToken);
                    await _sessionService.RemoveSessionAsync(sessionId);
                }
            }

            Response.Cookies.Delete(BionicProSessionOptions.CookieName);

            return Ok(new { message = "Logout successful" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("session")]
    public async Task<IActionResult> GetSessionInfo()
    {
        var sessionId = Request.Cookies[BionicProSessionOptions.CookieName];

        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(new { error = "No active session" });
        }

        var session = await _sessionService.GetSessionAsync(sessionId);

        if (session == null)
        {
            return Unauthorized(new { error = "Invalid session" });
        }

        return Ok(new
        {
            userId = session.UserId,
            username = session.Username,
            expiresAt = session.AccessTokenExpiresAt,
            refreshExpiresAt = session.RefreshTokenExpiresAt
        });
    }
}