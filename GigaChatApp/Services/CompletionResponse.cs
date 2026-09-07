namespace GigaChatApp.Services;

/// <summary>
/// Внутренний класс для десериализации ответа API.
/// </summary>
internal class CompletionResponse
{
    [JsonPropertyName("choices")]
    public List<ChoiceItem>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public Models.TokenUsage? Usage { get; set; }
}
