using BionicProAuth.Extensions;
using BionicProAuth.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BionicProAuth.Services;

public class KeycloakService
{
    private readonly HttpClient _httpClient;
    private readonly BionicProKeycloakOptions _keycloackOptions;
    private readonly ILogger<KeycloakService> _logger;

    public KeycloakService(
        HttpClient httpClient,
        IOptions<BionicProKeycloakOptions> keycloakOptions,
        ILogger<KeycloakService> logger)
    {
        _httpClient = httpClient;
        _keycloackOptions = keycloakOptions.Value;
        _logger = logger;
    }

    public async Task<TokenResponse?> AuthenticateAsync(string username, string password)
    {
        try
        {
            var tokenEndpoint = _keycloackOptions.GetOpenidConnectTokenUrl();

            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = _keycloackOptions.ClientId!,
                ["client_secret"] = _keycloackOptions.ClientSecret!,
                ["grant_type"] = "password",
                ["username"] = username,
                ["password"] = password
            };

            var content = new FormUrlEncodedContent(parameters);
            var response = await _httpClient.PostAsync(tokenEndpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Keycloak authentication failed: {Error}", errorContent);
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return tokenResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Keycloak authentication");
            return null;
        }
    }

    public async Task<TokenResponse?> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            var tokenEndpoint = _keycloackOptions.GetOpenidConnectTokenUrl();

            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = _keycloackOptions.ClientId!,
                ["client_secret"] = _keycloackOptions.ClientSecret!,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            };

            var content = new FormUrlEncodedContent(parameters);
            var response = await _httpClient.PostAsync(tokenEndpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Token refresh failed: {Error}", errorContent);
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return tokenResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return null;
        }
    }

    public async Task<bool> ValidateTokenAsync(string accessToken)
    {
        try
        {
            var introspectionEndpoint = _keycloackOptions.GetOpenidConnectTokenIntrospectUrl();

            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = _keycloackOptions.ClientId!,
                ["client_secret"] = _keycloackOptions.ClientSecret!,
                ["token"] = accessToken
            };

            var content = new FormUrlEncodedContent(parameters);
            var response = await _httpClient.PostAsync(introspectionEndpoint, content);

            if (!response.IsSuccessStatusCode)
                return false;

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonResponse);

            return document.RootElement.TryGetProperty("active", out var active) && active.GetBoolean();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token validation");
            return false;
        }
    }

    public async Task RevokeTokenAsync(string refreshToken)
    {
        try
        {
            var revokeEndpoint = _keycloackOptions.GetOpenidConnectLogoutUrl();

            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = _keycloackOptions.ClientId!,
                ["client_secret"] = _keycloackOptions.ClientSecret!,
                ["refresh_token"] = refreshToken
            };

            var content = new FormUrlEncodedContent(parameters);
            await _httpClient.PostAsync(revokeEndpoint, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token revocation");
        }
    }
}