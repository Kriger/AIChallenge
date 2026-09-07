namespace GigaChatApp.Services;

/// <summary>
/// Внутренний класс для десериализации элемента choices ответа выполнения подзадачи.
/// </summary>
internal class TaskChoiceItem
{
    [JsonPropertyName("message")]
    public Models.ApiMessage? Message { get; set; }
}
