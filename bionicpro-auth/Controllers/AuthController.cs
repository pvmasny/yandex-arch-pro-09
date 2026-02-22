using BionicProAuth.Models;
using BionicProAuth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

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
            var crmIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "crm_id")?.Value;
            int crmId = 0;
            if (!string.IsNullOrEmpty(crmIdClaim))
            {
                int.TryParse(crmIdClaim, out crmId);
            }
            // Создаем сессию
            var sessionId = await _sessionService.CreateSessionAsync(tokenResponse, username, userId, crmId);

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
            crmId = session.CrmId,
            username = session.Username,
            expiresAt = session.AccessTokenExpiresAt,
            refreshExpiresAt = session.RefreshTokenExpiresAt
        });
    }
    /// <summary>
    /// Валидация токена для микросервисов
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateToken()
    {
        try
        {
            // Получаем токен из заголовка Authorization
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                _logger.LogWarning("ValidateToken: No token provided");
                return Unauthorized(new ValidationResponse
                {
                    IsValid = false,
                    Error = "No token provided"
                });
            }

            var token = authHeader["Bearer ".Length..];

            _logger.LogDebug("Validating token: {TokenLength} chars", token.Length);

            // Сначала проверяем, есть ли активная сессия с таким токеном
            var session = await _sessionService.GetSessionByAccessTokenAsync(token);
            if (session != null)
            {
                _logger.LogInformation("Token validated via session for user {UserId}", session.UserId);

                return Ok(new ValidationResponse
                {
                    IsValid = true,
                    UserId = session.UserId,
                    CrmId = session.CrmId,
                    Username = session.Username,
                    Roles = session.Roles ?? new List<string>(),
                    ExpiresAt = session.AccessTokenExpiresAt,
                    Source = "session"
                });
            }

            // Если нет сессии, валидируем через Keycloak
            var isValid = await _keycloakService.ValidateTokenAsync(token);

            if (!isValid)
            {
                _logger.LogWarning("ValidateToken: Invalid token");
                return Unauthorized(new ValidationResponse
                {
                    IsValid = false,
                    Error = "Invalid token"
                });
            }

            // Извлекаем информацию из токена
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            var userId = jwtToken.Subject ??
                        jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

            var username = jwtToken.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
            var roles = ExtractRolesFromToken(jwtToken);
            var expiresAt = jwtToken.ValidTo;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("ValidateToken: No user ID in token");
                return Unauthorized(new ValidationResponse
                {
                    IsValid = false,
                    Error = "No user ID in token"
                });
            }
            var crmIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "crm_id")?.Value;
            int crmId = 0;
            if (!string.IsNullOrEmpty(crmIdClaim))
            {
                int.TryParse(crmIdClaim, out crmId);
            }

            _logger.LogInformation("Token validated via Keycloak for user {UserId}", userId);

            return Ok(new ValidationResponse
            {
                IsValid = true,
                UserId = userId,
                CrmId = crmId,
                Username = username,
                Roles = roles,
                ExpiresAt = expiresAt,
                Source = "keycloak"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return StatusCode(500, new ValidationResponse
            {
                IsValid = false,
                Error = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Проверка доступа к данным пользователя
    /// </summary>
    [HttpGet("validate-access")]
    public async Task<IActionResult> ValidateUserAccess([FromQuery] string targetUserId)
    {
        try
        {
            if (string.IsNullOrEmpty(targetUserId))
            {
                return BadRequest(new AccessValidationResponse
                {
                    HasAccess = false,
                    Error = "Target user ID is required"
                });
            }

            // Получаем токен из заголовка
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            string? currentUserId = null;
            List<string> roles = new();
            bool isAuthenticated = false;
            var crmId = 0;

            // Проверяем Bearer token (для микросервисов)
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader["Bearer ".Length..];

                // Проверяем сессию
                var session = await _sessionService.GetSessionByAccessTokenAsync(token);
                if (session != null)
                {
                    currentUserId = session.UserId;
                    roles = session.Roles ?? new();
                    isAuthenticated = true;
                    crmId = session.CrmId;
                    _logger.LogDebug("Access validation via session for user {UserId}", currentUserId);
                }
                else
                {
                    // Валидируем через Keycloak
                    var isValid = await _keycloakService.ValidateTokenAsync(token);
                    if (isValid)
                    {
                        var handler = new JwtSecurityTokenHandler();
                        var jwtToken = handler.ReadJwtToken(token);
                        currentUserId = jwtToken.Subject ??
                                       jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                        roles = ExtractRolesFromToken(jwtToken);
                        isAuthenticated = true;
                        var crmIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "crm_id")?.Value;
                        if (!string.IsNullOrEmpty(crmIdClaim))
                        {
                            int.TryParse(crmIdClaim, out crmId);
                        }
                        _logger.LogDebug("Access validation via Keycloak for user {UserId}", currentUserId);
                    }
                }
            }

            if (!isAuthenticated)
            {
                var sessionId = Request.Cookies[BionicProSessionOptions.CookieName];
                if (!string.IsNullOrEmpty(sessionId))
                {
                    var session = await _sessionService.GetSessionAsync(sessionId);
                    if (session != null)
                    {
                        currentUserId = session.UserId;
                        roles = session.Roles ?? new();
                        isAuthenticated = true;
                        crmId = session.CrmId;
                        _logger.LogDebug("Access validation via cookie for user {UserId}", currentUserId);
                    }
                }
            }

            if (!isAuthenticated || string.IsNullOrEmpty(currentUserId))
            {
                _logger.LogWarning("Access validation: Not authenticated");
                return Unauthorized(new AccessValidationResponse
                {
                    HasAccess = false,
                    Error = "Not authenticated"
                });
            }

            // Проверяем права доступа
            var isAdmin = roles.Contains("admin") || roles.Contains("reports-admin");

            // Пользователь может видеть только свои данные, админ - любые
            var hasAccess = isAdmin || currentUserId == targetUserId;

            _logger.LogInformation(
                "Access validation: User {CurrentUserId} {Action} access to {TargetUserId} (Admin: {IsAdmin})",
                currentUserId,
                hasAccess ? "GRANTED" : "DENIED",
                targetUserId,
                isAdmin);

            if (!hasAccess)
            {
                return Ok(new AccessValidationResponse
                {
                    HasAccess = false,
                    CrmId = crmId,
                    CurrentUserId = currentUserId,
                    IsAdmin = isAdmin,
                    Error = "Access denied. You can only access your own data."
                });
            }

            return Ok(new AccessValidationResponse
            {
                HasAccess = true,
                CurrentUserId = currentUserId,
                IsAdmin = isAdmin
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user access for target {TargetUserId}", targetUserId);
            return StatusCode(500, new AccessValidationResponse
            {
                HasAccess = false,
                Error = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Проверка доступа к данным пользователя
    /// </summary>
    [HttpGet("validate-access-by-session-id")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateUserBySessionIdAccess([FromQuery] string sessionId)
    {
        try
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return BadRequest(new AccessValidationResponse
                {
                    HasAccess = false,
                    Error = "Target user ID is required"
                });
            }

            var session = await _sessionService.GetSessionAsync(sessionId);
            if (session == null)
            {
                _logger.LogWarning("Access validation: Not authenticated");
                return Unauthorized(new AccessValidationResponse
                {
                    HasAccess = false,
                    Error = "Not authenticated"
                });
            }
            string currentUserId = null;
            var crmId = 0;
            var isAuthenticated = false;
            var roles = new List<string>();
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(session.AccessToken);
            currentUserId = jwtToken.Subject ??
                           jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            roles = ExtractRolesFromToken(jwtToken);
            isAuthenticated = true;
            var crmIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "crm_id")?.Value;
            if (!string.IsNullOrEmpty(crmIdClaim))
            {
                int.TryParse(crmIdClaim, out crmId);
            }
            _logger.LogDebug("Access validation via Keycloak for user {UserId}", currentUserId);



            if (!isAuthenticated || string.IsNullOrEmpty(currentUserId))
            {
                _logger.LogWarning("Access validation: Not authenticated");
                return Unauthorized(new AccessValidationResponse
                {
                    HasAccess = false,
                    Error = "Not authenticated"
                });
            }

            // Проверяем права доступа
            var isAdmin = roles.Contains("admin") || roles.Contains("reports-admin");

            // Пользователь может видеть только свои данные, админ - любые
            var hasAccess = isAdmin || currentUserId == session.UserId;

            

            if (!hasAccess)
            {
                return Ok(new AccessValidationResponse
                {
                    HasAccess = false,
                    CrmId = crmId,
                    CurrentUserId = currentUserId,
                    IsAdmin = isAdmin,
                    Token = session.AccessToken,
                    Error = "Access denied. You can only access your own data."
                });
            }

            return Ok(new AccessValidationResponse
            {
                HasAccess = true,
                CurrentUserId = currentUserId,
                CrmId = crmId,
                IsAdmin = isAdmin
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new AccessValidationResponse
            {
                HasAccess = false,
                Error = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Извлечение ролей из JWT токена
    /// </summary>
    private List<string> ExtractRolesFromToken(JwtSecurityToken token)
    {
        var roles = new List<string>();

        // Проверяем различные места где могут быть роли в Keycloak токене
        var realmAccess = token.Claims.FirstOrDefault(c => c.Type == "realm_access")?.Value;
        if (!string.IsNullOrEmpty(realmAccess))
        {
            try
            {
                // Парсим JSON из realm_access
                using var doc = JsonDocument.Parse(realmAccess);
                if (doc.RootElement.TryGetProperty("roles", out var rolesElement))
                {
                    foreach (var role in rolesElement.EnumerateArray())
                    {
                        roles.Add(role.GetString() ?? string.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse realm_access");
            }
        }

        // Проверяем прямые claims с ролями
        var roleClaims = token.Claims.Where(c =>
            c.Type == "roles" ||
            c.Type == "role" ||
            c.Type == ClaimTypes.Role).Select(c => c.Value);

        roles.AddRange(roleClaims);

        // Добавляем стандартные роли Keycloak
        var resourceAccess = token.Claims.FirstOrDefault(c => c.Type == "resource_access")?.Value;
        if (!string.IsNullOrEmpty(resourceAccess))
        {
            try
            {
                using var doc = JsonDocument.Parse(resourceAccess);
                // Парсим resource_access для клиента
                foreach (var resource in doc.RootElement.EnumerateObject())
                {
                    if (resource.Value.TryGetProperty("roles", out var resourceRoles))
                    {
                        foreach (var role in resourceRoles.EnumerateArray())
                        {
                            roles.Add(role.GetString() ?? string.Empty);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse resource_access");
            }
        }

        return roles.Distinct().ToList();
    }
}

// Response models
public class ValidationResponse
{
    public bool IsValid { get; set; }
    public string? UserId { get; set; }
    public int CrmId { get; set; }
    public string? Username { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime? ExpiresAt { get; set; }
    public string? Error { get; set; }
    public string? Source { get; set; } // "session" или "keycloak"
}

public class AccessValidationResponse
{
    public bool HasAccess { get; set; }
    public int CrmId { get; set; }
    public string? CurrentUserId { get; set; }
    public string? Token { get; set; }
    public bool IsAdmin { get; set; }
    public string? Error { get; set; }
}

public class UserInfoResponse
{
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool Authenticated { get; set; }
    public string? Error { get; set; }
    public string? Source { get; set; }
}