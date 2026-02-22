namespace report.Services;

using System.Net;
using System.Text;
using System.Text.Json;
using report.Models;
public class TokenRefreshResult
{
    public bool Success { get; set; }
    public string? NewToken { get; set; }
    public DateTime? NewExpiresAt { get; set; }
    public string? Error { get; set; }
}

public class UserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool IsActive { get; set; }
}
public class AuthServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthServiceClient> _logger;
    private readonly string _authServiceUrl;
    private readonly JsonSerializerOptions _jsonOptions;

    public AuthServiceClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AuthServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _authServiceUrl = "http://localhost:8000";// configuration["AuthService:Url"] ?? "http://auth:8000";
        _httpClient.BaseAddress = new Uri(_authServiceUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(5);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Валидация токена
    /// </summary>
    public async Task<TokenValidationResult> ValidateSesseionAsync(string sessionId)
    {
        try
        {
            _logger.LogInformation("Validating token with Auth Service");

            var url = $"/api/auth/validate-access-by-session-id?sessionId={Uri.EscapeDataString(sessionId)}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<TokenValidationResponse>(content, _jsonOptions);

                return new TokenValidationResult
                {
                    HasAccess = result?.HasAccess ?? true,
                    UserId = result?.UserId,
                    CrmId = result?.CrmId ?? 0,
                    Token = result?.Token,
                    Username = result?.Username,
                    Roles = result?.Roles ?? new(),
                    ExpiresAt = result?.ExpiresAt,
                    StatusCode = (int)response.StatusCode
                };
            }

            // Пробуем прочитать ошибку
            var errorContent = await response.Content.ReadAsStringAsync();

            return new TokenValidationResult
            {
                HasAccess = false,
                StatusCode = (int)response.StatusCode,
                Error = errorContent
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error validating token");
            return new TokenValidationResult
            {
                HasAccess = false,
                StatusCode = 503,
                Error = "Auth Service unavailable"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return new TokenValidationResult
            {
                HasAccess = false,
                StatusCode = 500,
                Error = "Internal error validating token"
            };
        }
    }

    /// <summary>
    /// Проверка доступа к данным пользователя
    /// </summary>
    public async Task<AccessValidationResult> ValidateUserAccessAsync(string token, string targetUserId)
    {
        try
        {
            _logger.LogInformation("Validating access for user {TargetUserId}", targetUserId);

            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/auth/validate-access?targetUserId={targetUserId}");
            request.Headers.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<AccessValidationResponse>(content, _jsonOptions);

                return new AccessValidationResult
                {
                    HasAccess = result?.HasAccess ?? false,
                    CurrentUserId = result?.CurrentUserId,
                    TargetUserId = targetUserId,
                    IsAdmin = result?.IsAdmin ?? false
                };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return new AccessValidationResult
                {
                    HasAccess = false,
                    Error = "Access denied"
                };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new AccessValidationResult
                {
                    HasAccess = false,
                    Error = "Unauthorized"
                };
            }

            return new AccessValidationResult
            {
                HasAccess = false,
                Error = $"Auth Service returned {response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user access");
            return new AccessValidationResult
            {
                HasAccess = false,
                Error = "Error validating access"
            };
        }
    }

    

    // Внутренние классы для десериализации ответов
    private class TokenValidationResponse
    {
        public bool HasAccess { get; set; }
        public string? UserId { get; set; }
        public string? Username { get; set; }
        public int CrmId { get; set; }
        public string Token { get; set; }
        public List<string> Roles { get; set; } = new();
        public DateTime? ExpiresAt { get; set; }
    }

    private class AccessValidationResponse
    {
        public bool HasAccess { get; set; }
        public string? CurrentUserId { get; set; }
        public bool IsAdmin { get; set; }
    }

    private class RefreshTokenResponse
    {
        public string? AccessToken { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}