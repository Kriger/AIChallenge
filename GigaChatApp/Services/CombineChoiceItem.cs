namespace GigaChatApp.Services;

/// <summary>
/// Внутренний класс для десериализации элемента choices ответа комбинирования результатов.
/// </summary>
internal class CombineChoiceItem
{
    [JsonPropertyName("message")]
    public Models.ApiMessage? Message { get; set; }
}
