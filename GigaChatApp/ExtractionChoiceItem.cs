namespace GigaChatApp;

/// <summary>
/// Внутренний класс для десериализации элемента choices ответа на извлечение фактов.
/// </summary>
internal class ExtractionChoiceItem
{
    [JsonPropertyName("message")]
    public Models.ApiMessage? Message { get; set; }
}
