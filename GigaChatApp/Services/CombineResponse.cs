namespace GigaChatApp.Services;

/// <summary>
/// Внутренний класс для десериализации ответа комбинирования результатов.
/// </summary>
internal class CombineResponse
{
    [JsonPropertyName("choices")]
    public List<CombineChoiceItem>? Choices { get; set; }
}
