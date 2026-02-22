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

    public async Task<string> CreateSessionAsync(TokenResponse tokenResponse, string username, string userId, int crmId)
    {
        return await CreateSessionAsync(tokenResponse, username, userId, crmId, null);
    }

    /// <summary>
    /// Создание сессии с ролями пользователя
    /// </summary>
    public async Task<string> CreateSessionAsync(TokenResponse tokenResponse, string username, string userId, int crmId, List<string>? roles = null)
    {
        var sessionId = GenerateSecureSessionId();

        // Шифруем refresh token перед сохранением
        var encryptedRefreshToken = _tokenEncryptionService.Encrypt(tokenResponse.RefreshToken);

        var sessionData = new SessionData
        {
            SessionId = sessionId,
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = encryptedRefreshToken,
            CrmId = crmId,
            UserId = userId,
            Username = username,
            Roles = roles ?? new List<string>(),
            AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
            RefreshTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.RefreshExpiresIn),
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        var serializedData = JsonSerializer.Serialize(sessionData);
        await _cache.SetStringAsync($"session:{sessionId}", serializedData, _sessionOptions);

        // Также сохраняем индекс по access token для быстрого поиска
        await _cache.SetStringAsync($"token:{tokenResponse.AccessToken}", sessionId, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) // Короткое время жизни для индекса
        });

        _logger.LogInformation("Session created for user {Username} with roles {Roles}", username, string.Join(",", roles ?? new()));
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

            // Обновляем время последней активности
            sessionData.LastActivityAt = DateTime.UtcNow;

            // Сохраняем обновленное время
            await UpdateSessionActivityAsync(sessionId);
        }

        return sessionData;
    }

    /// <summary>
    /// Получение сессии по access token (для микросервисов)
    /// </summary>
    public async Task<SessionData?> GetSessionByAccessTokenAsync(string accessToken)
    {
        // Получаем sessionId по индексу
        var sessionId = await _cache.GetStringAsync($"token:{accessToken}");

        if (string.IsNullOrEmpty(sessionId))
            return null;

        return await GetSessionAsync(sessionId);
    }

    /// <summary>
    /// Обновление времени последней активности сессии
    /// </summary>
    private async Task UpdateSessionActivityAsync(string sessionId)
    {
        var serializedData = await _cache.GetStringAsync($"session:{sessionId}");
        if (string.IsNullOrEmpty(serializedData))
            return;

        var sessionData = JsonSerializer.Deserialize<SessionData>(serializedData);
        if (sessionData != null)
        {
            sessionData.LastActivityAt = DateTime.UtcNow;
            var updatedData = JsonSerializer.Serialize(sessionData);
            await _cache.SetStringAsync($"session:{sessionId}", updatedData, _sessionOptions);
        }
    }

    public async Task UpdateSessionAsync(string sessionId, TokenResponse tokenResponse)
    {
        var sessionData = await GetSessionAsync(sessionId);
        if (sessionData == null)
            return;

        // Удаляем старый индекс по токену
        await _cache.RemoveAsync($"token:{sessionData.AccessToken}");

        sessionData.AccessToken = tokenResponse.AccessToken;
        sessionData.RefreshToken = _tokenEncryptionService.Encrypt(tokenResponse.RefreshToken);
        sessionData.AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        sessionData.RefreshTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.RefreshExpiresIn);
        sessionData.LastActivityAt = DateTime.UtcNow;

        var serializedData = JsonSerializer.Serialize(sessionData);
        await _cache.SetStringAsync($"session:{sessionId}", serializedData, _sessionOptions);

        // Создаем новый индекс по токену
        await _cache.SetStringAsync($"token:{tokenResponse.AccessToken}", sessionId, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        _logger.LogInformation("Session {SessionId} updated", sessionId);
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
        // Получаем данные сессии перед удалением
        var sessionData = await GetSessionAsync(sessionId);

        // Удаляем индекс по токену если есть
        if (sessionData?.AccessToken != null)
        {
            await _cache.RemoveAsync($"token:{sessionData.AccessToken}");
        }

        // Удаляем саму сессию
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

        // Refresh token уже расшифрован в GetSessionAsync, нужно снова зашифровать
        var encryptedRefreshToken = _tokenEncryptionService.Encrypt(sessionData.RefreshToken);
        sessionData.RefreshToken = encryptedRefreshToken;

        var serializedData = JsonSerializer.Serialize(sessionData);
        await _cache.SetStringAsync($"session:{newSessionId}", serializedData, _sessionOptions);

        // Создаем индекс по токену для новой сессии
        await _cache.SetStringAsync($"token:{sessionData.AccessToken}", newSessionId, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        _logger.LogInformation("Session rotated from {OldSessionId} to {NewSessionId}", oldSessionId, newSessionId);

        return newSessionId;
    }

    public async Task FinalRotateSessionAsync(string oldSessionId)
    {
        // Удаляем старую сессию и её индекс
        await RemoveSessionAsync(oldSessionId);
    }

    /// <summary>
    /// Проверка валидности сессии
    /// </summary>
    public async Task<bool> IsSessionValidAsync(string sessionId)
    {
        var session = await GetSessionAsync(sessionId);

        if (session == null)
            return false;

        // Проверяем, не истек ли refresh token
        if (session.RefreshTokenExpiresAt <= DateTime.UtcNow)
        {
            _logger.LogWarning("Session {SessionId} expired", sessionId);
            await RemoveSessionAsync(sessionId);
            return false;
        }

        // Если access token скоро истечет, пробуем обновить
        if (session.AccessTokenExpiresAt <= DateTime.UtcNow.AddMinutes(5))
        {
            _logger.LogInformation("Access token for session {SessionId} is about to expire, refreshing", sessionId);
            return await RefreshSessionTokenAsync(sessionId);
        }

        return true;
    }

    /// <summary>
    /// Получение всех активных сессий пользователя
    /// </summary>
    public async Task<List<SessionData>> GetUserSessionsAsync(string userId)
    {
        // В реальном приложении здесь должен быть поиск по индексу
        // Например, отдельный кэш с ключом user:{userId}:sessions
        _logger.LogWarning("GetUserSessionsAsync not fully implemented - would need user session index");
        return new List<SessionData>();
    }

    /// <summary>
    /// Завершение всех сессий пользователя (кроме текущей)
    /// </summary>
    public async Task TerminateOtherSessionsAsync(string userId, string currentSessionId)
    {
        var sessions = await GetUserSessionsAsync(userId);

        foreach (var session in sessions.Where(s => s.SessionId != currentSessionId))
        {
            await RemoveSessionAsync(session.SessionId);
        }

        _logger.LogInformation("Terminated {Count} other sessions for user {UserId}",
            sessions.Count(s => s.SessionId != currentSessionId), userId);
    }

    /// <summary>
    /// Очистка истекших сессий (для фоновой задачи)
    /// </summary>
    public async Task<int> CleanupExpiredSessionsAsync()
    {
        // В реальном приложении здесь должна быть логика поиска всех сессий
        // Например, с использованием Redis SCAN или отдельного индекса
        _logger.LogWarning("CleanupExpiredSessionsAsync not fully implemented - would need session enumeration");
        return 0;
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

/// <summary>
/// Расширенная модель данных сессии
/// </summary>
public class SessionData
{
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public int CrmId { get; set; } 
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public List<string> Roles { get; set; } = new();
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}