namespace GigaChatApp.Services;

/// <summary>
/// Внутренний класс для десериализации элемента choices ответа API.
/// </summary>
internal class ChoiceItem
{
    [JsonPropertyName("message")]
    public Models.ApiMessage? Message { get; set; }
}
