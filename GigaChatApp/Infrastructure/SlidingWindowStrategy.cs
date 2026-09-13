using GigaChatApp.Models;
using GigaChatApp.Services;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Стратегия 1: Sliding Window.
/// Хранит только последние N сообщений, всё остальное отбрасывается.
/// Никаких LLM-вызовов — простая и быстрая стратегия.
/// </summary>
public class SlidingWindowStrategy : IContextStrategy
{
    private readonly List<ApiMessage> _messages = new();
    private readonly int _windowSize;

    public string Name => "Sliding Window";

    public string Description => $"Хранит только последние {_windowSize} сообщений, остальное отбрасывается.";

    /// <summary>
    /// Общее количество добавленных сообщений (включая отброшенные).
    /// </summary>
    public int TotalAdded { get; private set; }

    /// <summary>
    /// Количество отброшенных сообщений.
    /// </summary>
    public int DroppedCount { get; private set; }

    public SlidingWindowStrategy(int windowSize = 10)
    {
        _windowSize = Math.Max(1, windowSize);
    }

    public void AddMessage(ApiMessage message)
    {
        _messages.Add(message);
        TotalAdded++;

        // Если превысили окно — отбрасываем старые
        if (_messages.Count > _windowSize)
        {
            var dropped = _messages.Count - _windowSize;
            _messages.RemoveRange(0, dropped);
            DroppedCount += dropped;
        }
    }

    public ContextResult BuildContext()
    {
        var originalTokens = TokenEstimator.EstimateHistoryTokens(_messages);
        var processedTokens = originalTokens;

        return new ContextResult
        {
            Messages = _messages.ToList(),
            OriginalTokens = originalTokens,
            CompressedTokens = processedTokens,
            Description = $"Sliding Window: {_messages.Count} сообщений (окно {_windowSize})",
            IsCompressed = DroppedCount > 0,
        };
    }

    public void Clear()
    {
        _messages.Clear();
        TotalAdded = 0;
        DroppedCount = 0;
    }

    public void LoadHistory(IEnumerable<ApiMessage> messages)
    {
        _messages.Clear();
        foreach (var msg in messages)
        {
            _messages.Add(msg);
        }

        // Применяем окно к загруженной истории
        if (_messages.Count > _windowSize)
        {
            var dropped = _messages.Count - _windowSize;
            _messages.RemoveRange(0, dropped);
            DroppedCount = dropped;
            TotalAdded = _messages.Count + dropped;
        }
    }

    public string GetStatus()
    {
        return $"""
            Стратегия: {Name}
            Размер окна: {_windowSize}
            Текущих сообщений: {_messages.Count}
            Всего добавлено: {TotalAdded}
            Отброшено: {DroppedCount}
            """;
    }
}
