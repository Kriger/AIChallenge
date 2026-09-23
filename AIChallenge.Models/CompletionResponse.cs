namespace AIChallenge.Models;

/// <summary>
/// Внутренний класс для десериализации ответа API.
/// </summary>
public class CompletionResponse
{
    [JsonPropertyName("choices")]
    public List<ChoiceItem>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public Models.TokenUsage? Usage { get; set; }
}
