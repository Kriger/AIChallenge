namespace AIChallenge.Models;

/// <summary>
/// Внутренний класс для десериализации ответа создания плана.
/// </summary>
public class PlanResponse
{
    [JsonPropertyName("choices")]
    public List<PlanChoiceItem>? Choices { get; set; }
}
