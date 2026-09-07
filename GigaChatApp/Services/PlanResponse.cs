namespace GigaChatApp.Services;

/// <summary>
/// Внутренний класс для десериализации ответа создания плана.
/// </summary>
internal class PlanResponse
{
    [JsonPropertyName("choices")]
    public List<PlanChoiceItem>? Choices { get; set; }
}
