namespace AIChallenge.Models;

/// <summary>
/// Внутренний класс для десериализации элемента choices ответа выполнения подзадачи.
/// </summary>
public class TaskChoiceItem
{
    [JsonPropertyName("message")]
    public Models.ApiMessage? Message { get; set; }
}
