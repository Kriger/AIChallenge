using GigaChatApp.Models;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Результат построения контекста для стратегии.
/// </summary>
public class ContextResult
{
    /// <summary>Сообщения для отправки в API (user/assistant).</summary>
    public List<ApiMessage> Messages { get; set; } = new();

    /// <summary>Сообщения с role="system" (summary блоков, facts и т.д.).</summary>
    public List<ApiMessage> SystemMessages { get; set; } = new();

    /// <summary>Текстовое summary контекста для подстановки в системное сообщение.</summary>
    public string SummaryText { get; set; } = string.Empty;

    /// <summary>Оценка токенов до обработки (вся история).</summary>
    public int OriginalTokens { get; set; }

    /// <summary>Оценка токенов после обработки (отправляемый контекст).</summary>
    public int CompressedTokens { get; set; }

    /// <summary>Сколько сообщений было заменено/обрезано.</summary>
    public int ReplacedMessages { get; set; }

    /// <summary>Описание того, что было сделано с контекстом.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>True, если контекст был обрезан/сжат.</summary>
    public bool IsCompressed { get; set; }

    /// <summary>Recent сообщения (alias для Messages для обратной совместимости).</summary>
    public List<ApiMessage> RecentMessages { get; set; } = new();
}
