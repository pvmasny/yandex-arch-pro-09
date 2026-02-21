using BionicProAuth.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace BionicProAuth.Services;

public class SessionService
{
    private readonly IDistributedCache _cache;
    private readonly KeycloakService _keycloakService;
    private readonly BionicProSessionOptions _options;
    private readonly ITokenEncryptionService _tokenEncryptionService;
    private readonly ILogger<SessionService> _logger;
    private readonly DistributedCacheEntryOptions _sessionOptions;

    public SessionService(
        IDistributedCache cache,
        KeycloakService keycloakService,
        IOptions<BionicProSessionOptions> options,
        ITokenEncryptionService tokenEncryptionService,
        IConfiguration configuration,
        ILogger<SessionService> logger)
    {
        _cache = cache;
        _options = options.Value;
        _tokenEncryptionService = tokenEncryptionService;
        _keycloakService = keycloakService;
        _logger = logger;

        var sessionTimeout = _options.TimeoutMinutes;
        _sessionOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(sessionTimeout)
        };
    }

    public async Task<string> CreateSessionAsync(TokenResponse tokenResponse, string username, string userId)
    {
        var sessionId = GenerateSecureSessionId();

        // Шифруем refresh token перед сохранением
        var encryptedRefreshToken = _tokenEncryptionService.Encrypt(tokenResponse.RefreshToken);

        var sessionData = new SessionData
        {
            SessionId = sessionId,
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = encryptedRefreshToken,
            UserId = userId,
            Username = username,
            AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
            RefreshTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.RefreshExpiresIn),
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        var serializedData = JsonSerializer.Serialize(sessionData);
        await _cache.SetStringAsync($"session:{sessionId}", serializedData, _sessionOptions);

        _logger.LogInformation("Session created for user {Username}", username);
        return sessionId;
    }

    public async Task<SessionData?> GetSessionAsync(string sessionId)
    {
        var serializedData = await _cache.GetStringAsync($"session:{sessionId}");

        if (string.IsNullOrEmpty(serializedData))
            return null;

        var sessionData = JsonSerializer.Deserialize<SessionData>(serializedData);

        if (sessionData != null)
        {
            // Дешифруем refresh token при получении
            sessionData.RefreshToken = _tokenEncryptionService.Decrypt(sessionData.RefreshToken);
        }

        return sessionData;
    }

    public async Task UpdateSessionAsync(string sessionId, TokenResponse tokenResponse)
    {
        var sessionData = await GetSessionAsync(sessionId);
        if (sessionData == null)
            return;

        sessionData.AccessToken = tokenResponse.AccessToken;
        sessionData.RefreshToken = _tokenEncryptionService.Encrypt(tokenResponse.RefreshToken);
        sessionData.AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        sessionData.RefreshTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.RefreshExpiresIn);
        sessionData.LastActivityAt = DateTime.UtcNow;

        var serializedData = JsonSerializer.Serialize(sessionData);
        await _cache.SetStringAsync($"session:{sessionId}", serializedData, _sessionOptions);
    }

    public async Task<bool> RefreshSessionTokenAsync(string sessionId)
    {
        try
        {
            var sessionData = await GetSessionAsync(sessionId);
            if (sessionData == null)
                return false;

            // Проверяем, не истек ли refresh token
            if (sessionData.RefreshTokenExpiresAt <= DateTime.UtcNow)
            {
                _logger.LogWarning("Refresh token expired for session {SessionId}", sessionId);
                await RemoveSessionAsync(sessionId);
                return false;
            }

            var newTokens = await _keycloakService.RefreshTokenAsync(sessionData.RefreshToken);
            if (newTokens == null)
            {
                await RemoveSessionAsync(sessionId);
                return false;
            }

            await UpdateSessionAsync(sessionId, newTokens);
            _logger.LogInformation("Session {SessionId} tokens refreshed", sessionId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task RemoveSessionAsync(string sessionId)
    {
        await _cache.RemoveAsync($"session:{sessionId}");
        _logger.LogInformation("Session {SessionId} removed", sessionId);
    }

    public async Task<string?> PreRotateSessionAsync(string oldSessionId)
    {
        var sessionData = await GetSessionAsync(oldSessionId);
        if (sessionData == null)
            return null;

        // Создаем новую сессию
        var newSessionId = GenerateSecureSessionId();

        // Копируем данные в новую сессию
        sessionData.SessionId = newSessionId;
        sessionData.LastActivityAt = DateTime.UtcNow;
        sessionData.RefreshToken = _tokenEncryptionService.Encrypt(sessionData.RefreshToken);

        var serializedData = JsonSerializer.Serialize(sessionData);
        await _cache.SetStringAsync($"session:{newSessionId}", serializedData, _sessionOptions);


        _logger.LogInformation("Session rotated from {OldSessionId} to {NewSessionId}", oldSessionId, newSessionId);

        return newSessionId;
    }

    public async Task FinalRotateSessionAsync(string oldSessionId)
    {
        // Удаляем старую сессию
        await _cache.RemoveAsync($"session:{oldSessionId}");

    }

    private string GenerateSecureSessionId()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("/", "_")
            .Replace("+", "-")
            .Replace("=", "");
    }
}