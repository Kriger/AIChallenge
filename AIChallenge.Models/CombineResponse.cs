namespace AIChallenge.Models;

/// <summary>
/// Внутренний класс для десериализации ответа комбинирования результатов.
/// </summary>
public class CombineResponse
{
    [JsonPropertyName("choices")]
    public List<CombineChoiceItem>? Choices { get; set; }
}
