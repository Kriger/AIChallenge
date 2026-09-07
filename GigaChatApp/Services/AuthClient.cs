using System.Net.Http.Headers;

namespace GigaChatApp.Services;

/// <summary>
/// Клиент OAuth2-аутентификации для GigaChat.
/// Получает Access Token по схеме OAuth2 Client Credentials.
/// </summary>
public class AuthClient
{
    private readonly HttpClient _httpClient;
    private readonly Models.GigaChatConfig _config;

    private string? _cachedToken;
    private DateTime _tokenExpiresAt;

    public AuthClient(HttpClient httpClient, Models.GigaChatConfig config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        await RefreshTokenAsync();
        return _cachedToken!;
    }

    private async Task RefreshTokenAsync()
    {
        var tokenUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_config.ClientId}:{_config.ClientSecret}")
        );

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("RqUID", Guid.NewGuid().ToString());

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("scope", _config.Scope),
        });

        var response = await _httpClient.PostAsync(tokenUrl, content);

        _httpClient.DefaultRequestHeaders.Authorization = null;

        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();

        if (tokenResponse?.AccessToken is null)
        {
            throw new InvalidOperationException(
                "Не удалось получить токен. Проверьте Client ID и Client Secret."
            );
        }

        _cachedToken = tokenResponse.AccessToken;

        if (tokenResponse.ExpiresAt.HasValue)
        {
            _tokenExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(tokenResponse.ExpiresAt.Value).UtcDateTime.AddSeconds(-30);
        }
        else
        {
            _tokenExpiresAt = DateTime.UtcNow.AddHours(1);
        }
    }
}
