namespace AIChallenge.Models;

/// <summary>
/// Внутренний класс для десериализации элемента choices ответа комбинирования результатов.
/// </summary>
public class CombineChoiceItem
{
    [JsonPropertyName("message")]
    public Models.ApiMessage? Message { get; set; }
}
