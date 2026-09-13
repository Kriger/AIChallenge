using GigaChatApp.Models;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Стратегия управления контекстом диалога.
/// </summary>
public enum ContextStrategy
{
    /// <summary>
    /// Sliding Window — только последние N сообщений, остальное отбрасывается.
    /// </summary>
    SlidingWindow,

    /// <summary>
    /// Sticky Facts — блок ключ-значение фактов + последние N сообщений.
    /// </summary>
    StickyFacts,

    /// <summary>
    /// Branching — ветвление диалога с checkpoints.
    /// </summary>
    Branching,
}

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

/// <summary>
/// Интерфейс стратегии управления контекстом.
/// </summary>
public interface IContextStrategy
{
    /// <summary>Название стратегии.</summary>
    string Name { get; }

    /// <summary>Описание стратегии.</summary>
    string Description { get; }

    /// <summary>Добавить сообщение в контекст.</summary>
    void AddMessage(ApiMessage message);

    /// <summary>
    /// Построить контекст для отправки в API.
    /// </summary>
    ContextResult BuildContext();

    /// <summary>Очистить весь контекст.</summary>
    void Clear();

    /// <summary>Загрузить историю из внешнего источника.</summary>
    void LoadHistory(IEnumerable<ApiMessage> messages);

    /// <summary>Получить информацию о текущем состоянии контекста.</summary>
    string GetStatus();
}
