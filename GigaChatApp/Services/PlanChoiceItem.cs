namespace GigaChatApp.Services;

/// <summary>
/// Внутренний класс для десериализации элемента choices ответа создания плана.
/// </summary>
internal class PlanChoiceItem
{
    [JsonPropertyName("message")]
    public Models.ApiMessage? Message { get; set; }
}
