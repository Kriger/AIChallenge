namespace GigaChatApp;

/// <summary>
/// Внутренний класс для десериализации ответа на извлечение фактов.
/// </summary>
internal class ExtractionResponse
{
    [JsonPropertyName("choices")]
    public List<ExtractionChoiceItem>? Choices { get; set; }
}
