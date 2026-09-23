namespace AIChallenge.Models;

/// <summary>
/// Конфигурация управления контекстом.
/// Определяет, как сжимается история диалога для экономии токенов.
/// </summary>
public class ContextManagerConfig
{
    /// <summary>
    /// Включено ли управление контекстом (legacy-флаг для summary-режима).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Количество последних сообщений, которые хранятся "как есть".
    /// Используется стратегиями SlidingWindow и StickyFacts.
    /// </summary>
    public int RecentMessageCount { get; set; } = 10;

    /// <summary>
    /// Интервал создания summary (каждые N сообщений).
    /// Используется в legacy summary-режиме.
    /// </summary>
    public int SummaryInterval { get; set; } = 10;

    /// <summary>
    /// Максимальное количество summary, которые хранятся.
    /// Используется в legacy summary-режиме.
    /// </summary>
    public int MaxSummaries { get; set; } = 20;

    /// <summary>
    /// Максимальное общее количество токенов для контекста (история + summary).
    /// 0 = без ограничения.
    /// </summary>
    public int MaxContextTokens { get; set; } = 0;

    /// <summary>
    /// Стратегия управления контекстом.
    /// </summary>
    public ContextStrategy Strategy { get; set; } = ContextStrategy.SlidingWindow;

    /// <summary>
    /// Промпт для генерации summary.
    /// </summary>
    public string SummaryPrompt { get; set; } =
        """
        Создай краткое содержимое (summary) следующего фрагмента диалога.
        Сохрани ключевые факты, решения, данные и контекст.
        Не включай приветствия, вежливые фразы и общие фразы.
        Формат: список ключевых пунктов.
        
        Диалог:
        {dialogue}
        
        Summary:
        """;

    /// <summary>
    /// Заголовок summary.
    /// </summary>
    public string SummaryHeader { get; set; } =
        "=== КОНТЕКСТ ИЗ ИСТОРИИ (блок {0}, {1}) ===\n{2}\n=========================================";
}
