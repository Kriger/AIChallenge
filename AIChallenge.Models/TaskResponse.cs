namespace AIChallenge.Models;

/// <summary>
/// Внутренний класс для десериализации ответа выполнения подзадачи.
/// </summary>
public class TaskResponse
{
    [JsonPropertyName("choices")]
    public List<TaskChoiceItem>? Choices { get; set; }
}
