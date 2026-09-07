namespace GigaChatApp.Infrastructure;

/// <summary>
/// Агрегированные метрики работы агента.
/// </summary>
public class AgentMetrics
{
    /// <summary>Общее количество запросов.</summary>
    public int TotalRequests { get; set; }

    /// <summary>Количество успешных запросов.</summary>
    public int SuccessfulRequests { get; set; }

    /// <summary>Количество запросов, завершившихся ошибкой.</summary>
    public int FailedRequests { get; set; }

    /// <summary>Количество запросов, возвращённых из кэша.</summary>
    public int CachedRequests { get; set; }

    /// <summary>Количество повторных попыток.</summary>
    public int RetryAttempts { get; set; }

    /// <summary>Максимальное количество повторных попыток.</summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>Задержка между повторными попытками (мс).</summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>Общее время работы (сумма всех запросов).</summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>Сумма prompt-токенов.</summary>
    public long TotalPromptTokens { get; set; }

    /// <summary>Сумма completion-токенов.</summary>
    public long TotalCompletionTokens { get; set; }

    /// <summary>Среднее время ответа.</summary>
    public TimeSpan AverageDuration =>
        TotalRequests > 0 ? TimeSpan.FromTicks(TotalDuration.Ticks / TotalRequests) : TimeSpan.Zero;

    /// <summary>Доля успешных запросов.</summary>
    public double SuccessRate =>
        TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0;
}
