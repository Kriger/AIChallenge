namespace GigaChatApp.Services;

/// <summary>
/// Внутренний класс для десериализации ответа OAuth2.
/// </summary>
internal class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_at")]
    public long? ExpiresAt { get; set; }
}
