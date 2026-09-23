namespace AIChallenge.Models;

/// <summary>
/// Общий ответ API с коллекцией вариантов (choices).
/// </summary>
public class ApiResponse<T>
{
    [JsonPropertyName("choices")]
    public List<T>? Choices { get; set; }
}

/// <summary>
/// Ответ API — завершённый чат.
/// </summary>
public class CompletionResponse : ApiResponse<ChoiceItem>
{
    [JsonPropertyName("usage")]
    public TokenUsage? Usage { get; set; }
}

/// <summary>
/// Ответ API — создание плана.
/// </summary>
public class PlanResponse : ApiResponse<ChoiceItem> { }

/// <summary>
/// Ответ API — выполнение подзадачи.
/// </summary>
public class TaskResponse : ApiResponse<ChoiceItem> { }

/// <summary>
/// Ответ API — комбинирование результатов.
/// </summary>
public class CombineResponse : ApiResponse<ChoiceItem> { }

/// <summary>
/// Ответ API — извлечение фактов.
/// </summary>
public class ExtractionResponse : ApiResponse<ChoiceItem> { }
