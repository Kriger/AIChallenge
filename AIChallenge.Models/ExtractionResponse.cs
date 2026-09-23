namespace AIChallenge.Models;

/// <summary>
/// Внутренний класс для десериализации ответа на извлечение фактов.
/// </summary>
public class ExtractionResponse
{
    [JsonPropertyName("choices")]
    public List<ExtractionChoiceItem>? Choices { get; set; }
}
