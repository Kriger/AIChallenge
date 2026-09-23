namespace AIChallenge.Models;

/// <summary>
/// Внутренний класс для десериализации элемента choices ответа API.
/// </summary>
public class ChoiceItem
{
    [JsonPropertyName("message")]
    public Models.ApiMessage? Message { get; set; }
}
