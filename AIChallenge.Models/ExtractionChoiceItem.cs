namespace AIChallenge.Models;

/// <summary>
/// Внутренний класс для десериализации элемента choices ответа на извлечение фактов.
/// </summary>
public class ExtractionChoiceItem
{
    [JsonPropertyName("message")]
    public AIChallenge.Models.ApiMessage? Message { get; set; }
}
