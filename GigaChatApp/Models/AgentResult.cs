namespace GigaChatApp.Models;

/// <summary>
/// Результат обработки запроса агентом.
/// </summary>
public class AgentResult
{
    /// <summary>Ответ LLM.</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>Время генерации ответа.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Использование токенов.</summary>
    public TokenUsage? Usage { get; set; }

    /// <summary>Количество токенов текущего запроса пользователя (оценка).</summary>
    public int CurrentRequestTokens { get; set; }

    /// <summary>Количество токенов всей истории диалога (оценка).</summary>
    public int HistoryTokens { get; set; }

    /// <summary>Сообщение об ошибке, если запрос не удался.</summary>
    public string? Error { get; set; }

    /// <summary>Из какого источника получен ответ.</summary>
    public Source Source { get; set; } = Source.Api;

    /// <summary>True, если запрос завершился успешно.</summary>
    public bool IsSuccess => Error is null;
}
