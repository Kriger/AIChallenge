namespace AIChallenge.Models;

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
