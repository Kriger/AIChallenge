namespace AIChallenge.Models;

/// <summary>
/// Внутренний класс для десериализации ответа OAuth2.
/// </summary>
public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_at")]
    public long? ExpiresAt { get; set; }
}
