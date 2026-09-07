namespace GigaChatApp.Services;

/// <summary>
/// Внутренний класс для десериализации ответа выполнения подзадачи.
/// </summary>
internal class TaskResponse
{
    [JsonPropertyName("choices")]
    public List<TaskChoiceItem>? Choices { get; set; }
}
