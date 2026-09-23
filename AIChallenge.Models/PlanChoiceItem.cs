namespace AIChallenge.Models;

/// <summary>
/// Внутренний класс для десериализации элемента choices ответа создания плана.
/// </summary>
public class PlanChoiceItem
{
    [JsonPropertyName("message")]
    public Models.ApiMessage? Message { get; set; }
}
