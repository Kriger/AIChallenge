using GigaChatApp.Models;
using GigaChatApp.Services;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Сервис управления контекстом диалога.
/// Поддерживает три стратегии:
/// 1. SlidingWindow — только последние N сообщений
/// 2. StickyFacts — факты + последние N сообщений
/// 3. Branching — ветвление диалога с checkpoints
///
/// Также поддерживает legacy-режим с summary (старая логика).
/// </summary>
public class ContextManager
{
    private readonly ChatClient _chatClient;
    private readonly AuthClient _authClient;
    private readonly AgentLogger _logger;
    private readonly ContextManagerConfig _config;

    // Legacy fields для summary-режима
    private readonly List<ApiMessage> _fullHistory = new();
    private readonly List<SummaryBlock> _summaries = new();

    // Стратегии
    private readonly SlidingWindowStrategy? _slidingWindow;
    private readonly StickyFactsStrategy? _stickyFacts;
    private readonly BranchingStrategy? _branching;

    /// <summary>Метрики сравнения (для legacy summary-режима).</summary>
    public ContextComparisonMetrics ComparisonMetrics { get; } = new();

    /// <summary>Конфигурация.</summary>
    public ContextManagerConfig Config => _config;

    /// <summary>Текущая стратегия.</summary>
    public ContextStrategy CurrentStrategy { get; private set; }

    /// <summary>Общее количество сообщений в истории (legacy).</summary>
    public int TotalHistoryCount => _fullHistory.Count;

    /// <summary>Количество recent сообщений.</summary>
    public int RecentCount => Math.Min(_config.RecentMessageCount, _fullHistory.Count);

    /// <summary>Количество summary блоков.</summary>
    public int SummaryCount => _summaries.Count;

    public ContextManager(
        ChatClient chatClient,
        AuthClient authClient,
        AgentLogger logger,
        ContextManagerConfig? config = null,
        int? stickyFactsWindowSize = null,
        int? stickyFactsMaxFacts = null)
    {
        _chatClient = chatClient;
        _authClient = authClient;
        _logger = logger;
        _config = config ?? new ContextManagerConfig();

        // Инициализируем все стратегии с настройками из конфига
        var slidingWindowSize = _config.RecentMessageCount;
        _slidingWindow = new SlidingWindowStrategy(slidingWindowSize);

        var stickyWindowSize = stickyFactsWindowSize ?? _config.RecentMessageCount;
        var stickyMaxFacts = stickyFactsMaxFacts ?? 50;
        _stickyFacts = new StickyFactsStrategy(chatClient, authClient, logger, stickyWindowSize, stickyMaxFacts);

        _branching = new BranchingStrategy();

        // По умолчанию — SlidingWindow
        CurrentStrategy = ContextStrategy.SlidingWindow;
    }

    /// <summary>
    /// Переключить стратегию управления контекстом.
    /// </summary>
    public void SetStrategy(ContextStrategy strategy)
    {
        var prev = CurrentStrategy;
        CurrentStrategy = strategy;

        var strategyName = strategy switch
        {
            ContextStrategy.SlidingWindow => "Sliding Window",
            ContextStrategy.StickyFacts => "Sticky Facts",
            ContextStrategy.Branching => "Branching",
            _ => "Unknown"
        };

        _logger.Info($"Стратегия контекста: {prev} → {strategyName}");
    }

    /// <summary>
    /// Добавляет сообщение в историю.
    /// Перенаправляет на активную стратегию.
    /// </summary>
    public void AddMessage(ApiMessage message)
    {
        switch (CurrentStrategy)
        {
            case ContextStrategy.SlidingWindow:
                _slidingWindow!.AddMessage(message);
                break;
            case ContextStrategy.StickyFacts:
                _stickyFacts!.AddMessage(message);
                break;
            case ContextStrategy.Branching:
                _branching!.AddMessage(message);
                break;
            default:
                // Legacy: добавляем в полную историю
                _fullHistory.Add(message);
                break;
        }
    }

    /// <summary>
    /// Формирует контекст для отправки в API.
    /// Перенаправляет на активную стратегию.
    /// </summary>
    public ContextResult BuildContext()
    {
        var result = new ContextResult();

        switch (CurrentStrategy)
        {
            case ContextStrategy.SlidingWindow:
                result = BuildFromSlidingWindow();
                break;
            case ContextStrategy.StickyFacts:
                result = BuildFromStickyFacts();
                break;
            case ContextStrategy.Branching:
                result = BuildFromBranching();
                break;
            default:
                // Legacy: summary-режим
                result = BuildLegacyContext();
                break;
        }

        // Записываем метрики сравнения (для legacy)
        if (CurrentStrategy != ContextStrategy.SlidingWindow &&
            CurrentStrategy != ContextStrategy.Branching)
        {
            ComparisonMetrics.RecordComparison(
                result.OriginalTokens,
                result.CompressedTokens,
                result.ReplacedMessages,
                result.SystemMessages.Count + result.RecentMessages.Count);
        }

        return result;
    }

    // ==================== Sliding Window ====================

    private ContextResult BuildFromSlidingWindow()
    {
        var swResult = _slidingWindow!.BuildContext();
        return new ContextResult
        {
            RecentMessages = swResult.Messages,
            SystemMessages = swResult.SystemMessages,
            SummaryText = swResult.SummaryText,
            OriginalTokens = swResult.OriginalTokens,
            CompressedTokens = swResult.CompressedTokens,
            ReplacedMessages = swResult.OriginalTokens - swResult.CompressedTokens,
            IsCompressed = swResult.IsCompressed,
        };
    }

    // ==================== Sticky Facts ====================

    private ContextResult BuildFromStickyFacts()
    {
        var sfResult = _stickyFacts!.BuildContext();
        return new ContextResult
        {
            RecentMessages = sfResult.Messages,
            SystemMessages = sfResult.SystemMessages,
            SummaryText = sfResult.SummaryText,
            OriginalTokens = sfResult.OriginalTokens,
            CompressedTokens = sfResult.CompressedTokens,
            ReplacedMessages = 0,
            IsCompressed = sfResult.IsCompressed,
        };
    }

    // ==================== Branching ====================

    private ContextResult BuildFromBranching()
    {
        var brResult = _branching!.BuildContext();
        return new ContextResult
        {
            RecentMessages = brResult.Messages,
            SystemMessages = brResult.SystemMessages,
            SummaryText = brResult.SummaryText,
            OriginalTokens = brResult.OriginalTokens,
            CompressedTokens = brResult.CompressedTokens,
            ReplacedMessages = 0,
            IsCompressed = brResult.IsCompressed,
        };
    }

    // ==================== Legacy Summary Mode ====================

    private ContextResult BuildLegacyContext()
    {
        if (!_config.Enabled || _summaries.Count == 0)
        {
            var tokens = TokenEstimator.EstimateHistoryTokens(_fullHistory);
            return new ContextResult
            {
                RecentMessages = _fullHistory.ToList(),
                OriginalTokens = tokens,
                CompressedTokens = tokens,
            };
        }

        var recentCount = Math.Min(_config.RecentMessageCount, _fullHistory.Count);
        var recentMessages = _fullHistory.Skip(_fullHistory.Count - recentCount).ToList();
        var compressedMessages = _fullHistory.Take(_fullHistory.Count - recentCount).ToList();

        var summaryText = BuildSummaryText();
        var systemMessages = new List<ApiMessage>();
        foreach (var summary in _summaries)
        {
            systemMessages.Add(new ApiMessage
            {
                Role = "system",
                Content = summary.SummaryText,
            });
        }

        var originalTokens = TokenEstimator.EstimateHistoryTokens(_fullHistory);
        var compressedTokens = TokenEstimator.EstimateHistoryTokens(recentMessages);

        return new ContextResult
        {
            SystemMessages = systemMessages,
            RecentMessages = recentMessages,
            SummaryText = summaryText,
            OriginalTokens = originalTokens,
            CompressedTokens = compressedTokens,
            ReplacedMessages = compressedMessages.Count,
        };
    }

    private string BuildSummaryText()
    {
        if (_summaries.Count == 0)
            return string.Empty;
        return string.Join("\n\n", _summaries.Select(s => s.SummaryText));
    }

    // ==================== Legacy Summary Compression ====================

    /// <summary>
    /// Генерирует summary для старых сообщений через LLM (legacy-режим).
    /// </summary>
    public async Task CompressOldMessagesAsync()
    {
        try
        {
            await Task.Delay(500);

            var token = await _authClient.GetAccessTokenAsync();

            var recentCount = Math.Min(_config.RecentMessageCount, _fullHistory.Count);
            var messagesToCompress = _fullHistory
                .Take(_fullHistory.Count - recentCount)
                .Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (messagesToCompress.Count == 0)
                return;

            var dialogueText = string.Join("\n", messagesToCompress.Select(m =>
                $"{(m.Role == "user" ? "Пользователь" : "Ассистент")}: {m.Content}"));

            var prompt = _config.SummaryPrompt.Replace("{dialogue}", dialogueText);

            var messages = new List<ApiMessage>
            {
                new() { Role = "user", Content = prompt },
            };

            var requestObj = new Dictionary<string, object>
            {
                ["model"] = "GigaChat-2",
                ["messages"] = messages.Select(m => new { m.Role, m.Content }).ToList<object>(),
                ["stream"] = false,
                ["temperature"] = 0.1,
            };

            var maxRetries = 3;
            var retryDelay = 1000;
            Exception? lastException = null;

            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    httpClient.BaseAddress = new Uri("https://api.giga.chat");

                    var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", requestObj);
                    httpClient.Dispose();

                    if (response.IsSuccessStatusCode)
                    {
                        var parsed = await response.Content.ReadFromJsonAsync<CompletionResponse>();
                        if (parsed?.Choices?.Count == 0)
                        {
                            _logger.Warning("Summary: пустой ответ от LLM");
                            return;
                        }

                        var summaryContent = parsed?.Choices?[0].Message?.Content ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(summaryContent))
                            return;

                        var blockNumber = _summaries.Count + 1;
                        var timeRange = $"{messagesToCompress.First().Role} → {messagesToCompress.Last().Role} " +
                                       $"({messagesToCompress.Count} сообщений)";

                        var summaryBlock = new SummaryBlock
                        {
                            BlockNumber = blockNumber,
                            TimeRange = timeRange,
                            ReplacedCount = messagesToCompress.Count,
                            SummaryText = string.Format(
                                _config.SummaryHeader,
                                blockNumber,
                                timeRange,
                                summaryContent),
                            CreatedAt = DateTime.UtcNow,
                        };

                        _summaries.Add(summaryBlock);

                        while (_summaries.Count > _config.MaxSummaries)
                        {
                            _summaries.RemoveAt(0);
                            _logger.Info("Удалён старый summary блок (лимит)");
                        }

                        _logger.Info($"Summary создан: блок {blockNumber}, " +
                                    $"заменено {messagesToCompress.Count} сообщений");
                        return;
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxRetries)
                    {
                        _logger.Warning($"Summary: TooManyRequests, повтор через {retryDelay} мс");
                        await Task.Delay(retryDelay);
                        retryDelay *= 2;
                    }
                    else
                    {
                        _logger.Warning($"Ошибка генерации summary: HTTP {response.StatusCode}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt < maxRetries)
                    {
                        _logger.Warning($"Summary: ошибка, повтор через {retryDelay} мс ({ex.GetType().Name})");
                        await Task.Delay(retryDelay);
                        retryDelay *= 2;
                    }
                }
            }

            if (lastException != null)
            {
                _logger.Error($"Ошибка генерации summary после {maxRetries} попыток: {lastException.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Ошибка генерации summary: {ex.Message}");
        }
    }

    // ==================== Common Operations ====================

    /// <summary>
    /// Очищает историю и summary.
    /// </summary>
    public void Clear()
    {
        _fullHistory.Clear();
        _summaries.Clear();
        _slidingWindow?.Clear();
        _stickyFacts?.Clear();
        _branching?.Clear();
        _logger.Info("Контекст очищен");
    }

    /// <summary>
    /// Загружает историю из внешнего источника.
    /// </summary>
    public void LoadHistory(IEnumerable<ApiMessage> messages)
    {
        _fullHistory.Clear();
        foreach (var msg in messages)
        {
            _fullHistory.Add(msg);
        }

        switch (CurrentStrategy)
        {
            case ContextStrategy.SlidingWindow:
                _slidingWindow!.LoadHistory(messages);
                break;
            case ContextStrategy.StickyFacts:
                _stickyFacts!.LoadHistory(messages);
                break;
            case ContextStrategy.Branching:
                _branching!.LoadHistory(messages);
                break;
        }

        _logger.Info($"Загружено {messages.Count()} сообщений истории");
    }

    /// <summary>
    /// Получает все summary блоки (для отладки/сохранения).
    /// </summary>
    public IReadOnlyList<SummaryBlock> GetSummaries() => _summaries.AsReadOnly();

    /// <summary>
    /// Добавляет summary блок (для восстановления из сохранения).
    /// </summary>
    public void AddSummaryBlock(SummaryBlock block)
    {
        _summaries.Add(block);
    }

    /// <summary>
    /// Переключает режим сжатия (legacy).
    /// </summary>
    public void ToggleCompression()
    {
        _config.Enabled = !_config.Enabled;
        var status = _config.Enabled ? "включено" : "выключено";
        _logger.Info($"Управление контекстом: {status}");
    }

    // ==================== Strategy-Specific Accessors ====================

    /// <summary>
    /// Получить SlidingWindowStrategy для прямого доступа.
    /// </summary>
    public SlidingWindowStrategy? SlidingWindow => _slidingWindow;

    /// <summary>
    /// Получить StickyFactsStrategy для прямого доступа.
    /// </summary>
    public StickyFactsStrategy? StickyFacts => _stickyFacts;

    /// <summary>
    /// Получить BranchingStrategy для прямого доступа.
    /// </summary>
    public BranchingStrategy? Branching => _branching;

    /// <summary>
    /// Получить статус активной стратегии.
    /// </summary>
    public string GetStrategyStatus()
    {
        return CurrentStrategy switch
        {
            ContextStrategy.SlidingWindow => _slidingWindow?.GetStatus() ?? "",
            ContextStrategy.StickyFacts => _stickyFacts?.GetStatus() ?? "",
            ContextStrategy.Branching => _branching?.GetStatus() ?? "",
            _ => "Неизвестная стратегия",
        };
    }

    /// <summary>
    /// Блок summary.
    /// </summary>
    public class SummaryBlock
    {
        public int BlockNumber { get; set; }
        public string TimeRange { get; set; } = string.Empty;
        public int ReplacedCount { get; set; }
        public string SummaryText { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}

/// <summary>
/// Метрики сравнения контекста: сжатие vs без сжатия.
/// Используется для оценки качества и экономии токенов.
/// </summary>
public class ContextComparisonMetrics
{
    /// <summary>Количество сравнений.</summary>
    public int ComparisonCount { get; set; }

    /// <summary>Сумма токенов до сжатия.</summary>
    public long TotalOriginalTokens { get; set; }

    /// <summary>Сумма токенов после сжатия.</summary>
    public long TotalCompressedTokens { get; set; }

    /// <summary>Сумма заменённых сообщений.</summary>
    public long TotalReplacedMessages { get; set; }

    /// <summary>Сумма отправленных сообщений.</summary>
    public long TotalSentMessages { get; set; }

    /// <summary>Максимальная экономия токенов (за один запрос).</summary>
    public long MaxTokenSavings { get; set; }

    /// <summary>Средняя экономия токенов.</summary>
    public long AverageTokenSavings =>
        ComparisonCount > 0 ? TotalTokenSavings / ComparisonCount : 0;

    /// <summary>Общая экономия токенов.</summary>
    public long TotalTokenSavings => TotalOriginalTokens - TotalCompressedTokens;

    /// <summary>Процент экономии токенов.</summary>
    public double TokenSavingsPercent =>
        TotalOriginalTokens > 0 ? (double)TotalTokenSavings / TotalOriginalTokens * 100 : 0;

    /// <summary>Записывает результат одного сравнения.</summary>
    public void RecordComparison(int originalTokens, int compressedTokens,
        int replacedMessages, int sentMessages)
    {
        ComparisonCount++;
        TotalOriginalTokens += originalTokens;
        TotalCompressedTokens += compressedTokens;
        TotalReplacedMessages += replacedMessages;
        TotalSentMessages += sentMessages;

        var savings = originalTokens - compressedTokens;
        if (savings > MaxTokenSavings)
            MaxTokenSavings = savings;
    }

    /// <summary>Сбрасывает метрики.</summary>
    public void Reset()
    {
        ComparisonCount = 0;
        TotalOriginalTokens = 0;
        TotalCompressedTokens = 0;
        TotalReplacedMessages = 0;
        TotalSentMessages = 0;
        MaxTokenSavings = 0;
    }

    /// <summary>Формирует строку отчёта.</summary>
    public string GetReport()
    {
        return $"""
            === Метрики управления контекстом ===
            Сравнений: {ComparisonCount}
            Токены до:     {TotalOriginalTokens,10:N0}
            Токены после:  {TotalCompressedTokens,10:N0}
            Экономия:      {TotalTokenSavings,10:N0} ({TokenSavingsPercent:F1}%)
            Avg экономия:  {AverageTokenSavings,10:N0}
            Max экономия:  {MaxTokenSavings,10:N0}
            Заменено сообщ.: {TotalReplacedMessages,8:N0}
            Отправлено:      {TotalSentMessages,8:N0}
            ====================================
            """;
    }
}
