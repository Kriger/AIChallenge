using GigaChatApp.Models;
using GigaChatApp.Services;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Результат обработки контекста: какие сообщения отправить + summary.
/// </summary>
public class ContextResult
{
    /// <summary>Сообщения с role="system" (summary блоков).</summary>
    public List<ApiMessage> SystemMessages { get; set; } = new();

    /// <summary>Сообщения с role="user"/"assistant" (recent сообщения).</summary>
    public List<ApiMessage> RecentMessages { get; set; } = new();

    /// <summary>Summary текста, который подставляется в системное сообщение.</summary>
    public string SummaryText { get; set; } = string.Empty;

    /// <summary>Оценка токенов до сжатия (вся история).</summary>
    public int OriginalTokens { get; set; }

    /// <summary>Оценка токенов после сжатия (recent + summary).</summary>
    public int CompressedTokens { get; set; }

    /// <summary>Сколько сообщений было заменено на summary.</summary>
    public int ReplacedMessages { get; set; }

    /// <summary>True, если сжатие было применено.</summary>
    public bool IsCompressed => ReplacedMessages > 0;
}

/// <summary>
/// Сервис управления контекстом диалога.
/// Хранит последние N сообщений "как есть", остальное заменяет на summary.
/// Позволяет сравнивать качество ответов до/после сжатия.
/// </summary>
public class ContextManager
{
    private readonly ChatClient _chatClient;
    private readonly AuthClient _authClient;
    private readonly AgentLogger _logger;
    private readonly ContextManagerConfig _config;

    /// <summary>Все сообщения истории (неизменяемые).</summary>
    private readonly List<ApiMessage> _fullHistory = new();

    /// <summary>Сгенерированные summary блоков.</summary>
    private readonly List<SummaryBlock> _summaries = new();

    /// <summary>Метрики сравнения.</summary>
    public ContextComparisonMetrics ComparisonMetrics { get; } = new();

    /// <summary>Конфигурация.</summary>
    public ContextManagerConfig Config => _config;

    /// <summary>Общее количество сообщений в истории.</summary>
    public int TotalHistoryCount => _fullHistory.Count;

    /// <summary>Количество recent сообщений.</summary>
    public int RecentCount => Math.Min(_config.RecentMessageCount, _fullHistory.Count);

    /// <summary>Количество summary блоков.</summary>
    public int SummaryCount => _summaries.Count;

    public ContextManager(
        ChatClient chatClient,
        AuthClient authClient,
        AgentLogger logger,
        ContextManagerConfig? config = null)
    {
        _chatClient = chatClient;
        _authClient = authClient;
        _logger = logger;
        _config = config ?? new ContextManagerConfig();
    }

    /// <summary>
    /// Добавляет сообщение в историю.
    /// Если включено сжатие — проверяет, нужно ли создать summary.
    /// </summary>
    public void AddMessage(ApiMessage message)
    {
        _fullHistory.Add(message);

        if (!_config.Enabled)
            return;

        // Проверяем, пора ли создавать summary
        var nonSummaryCount = _fullHistory.Count - _summaries.Sum(s => s.ReplacedCount);
        if (nonSummaryCount >= _config.SummaryInterval)
        {
            CompressOldMessages();
        }
    }

    /// <summary>
    /// Формирует контекст для отправки в API.
    /// Возвращает recent сообщения + summary для старых.
    /// </summary>
    public ContextResult BuildContext()
    {
        if (!_config.Enabled || _summaries.Count == 0)
        {
            // Нет сжатия — возвращаем полную историю (все user/assistant)
            var tokens = TokenEstimator.EstimateHistoryTokens(_fullHistory);
            return new ContextResult
            {
                RecentMessages = _fullHistory.ToList(),
                OriginalTokens = tokens,
                CompressedTokens = tokens,
            };
        }

        // Считаем, сколько сообщений — это "recent"
        var recentCount = Math.Min(_config.RecentMessageCount, _fullHistory.Count);

        // Находим, где заканчивается recent-зона
        var recentMessages = _fullHistory.Skip(_fullHistory.Count - recentCount).ToList();
        var compressedMessages = _fullHistory.Take(_fullHistory.Count - recentCount).ToList();

        // Формируем summary текст
        var summaryText = BuildSummaryText();

        // Собираем summary как system-сообщения
        var systemMessages = new List<ApiMessage>();
        foreach (var summary in _summaries)
        {
            systemMessages.Add(new ApiMessage
            {
                Role = "system",
                Content = summary.SummaryText,
            });
        }

        // Считаем токены
        var originalTokens = TokenEstimator.EstimateHistoryTokens(_fullHistory);
        var compressedTokens = TokenEstimator.EstimateHistoryTokens(recentMessages);

        var result = new ContextResult
        {
            SystemMessages = systemMessages,
            RecentMessages = recentMessages,
            SummaryText = summaryText,
            OriginalTokens = originalTokens,
            CompressedTokens = compressedTokens,
            ReplacedMessages = compressedMessages.Count,
        };

        // Записываем метрики сравнения
        ComparisonMetrics.RecordComparison(
            originalTokens,
            compressedTokens,
            compressedMessages.Count,
            systemMessages.Count + recentMessages.Count);

        _logger.Info($"Контекст: {originalTokens} → {compressedTokens} токенов " +
                     $"(заменено {compressedMessages.Count} сообщений, " +
                     $"{systemMessages.Count} summary + {recentMessages.Count} recent отправлено)");

        return result;
    }

    /// <summary>
    /// Генерирует summary для старых сообщений через LLM.
    /// Имеет retry-логику для обработки TooManyRequests.
    /// </summary>
    private async void CompressOldMessages()
    {
        try
        {
            // Небольшая задержка, чтобы не перегружать API
            await Task.Delay(500);

            var token = await _authClient.GetAccessTokenAsync();

            // Находим сообщения, которые нужно сжать
            var recentCount = Math.Min(_config.RecentMessageCount, _fullHistory.Count);
            var messagesToCompress = _fullHistory
                .Take(_fullHistory.Count - recentCount)
                .Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (messagesToCompress.Count == 0)
                return;

            // Формируем текст диалога
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

            // Retry-логика для обработки TooManyRequests
            var maxRetries = 3;
            var retryDelay = 1000; // мс
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

                        var summaryContent = parsed.Choices[0].Message?.Content ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(summaryContent))
                            return;

                        // Формируем summary блок
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

                        // Ограничиваем количество summary
                        while (_summaries.Count > _config.MaxSummaries)
                        {
                            _summaries.RemoveAt(0);
                            _logger.Info("Удалён старый summary блок (лимит)");
                        }

                        _logger.Info($"Summary создан: блок {blockNumber}, " +
                                    $"заменено {messagesToCompress.Count} сообщений");
                        return; // Успех — выходим
                    }

                    // Проверяем, стоит ли retry
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxRetries)
                    {
                        _logger.Warning($"Summary: TooManyRequests, повтор через {retryDelay} мс (попытка {attempt + 1})");
                        await Task.Delay(retryDelay);
                        retryDelay *= 2; // Экспоненциальная задержка
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

            // Все попытки исчерпаны
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

    /// <summary>
    /// Формирует объединённый текст всех summary.
    /// </summary>
    private string BuildSummaryText()
    {
        if (_summaries.Count == 0)
            return string.Empty;

        return string.Join("\n\n", _summaries.Select(s => s.SummaryText));
    }

    /// <summary>
    /// Очищает историю и summary.
    /// </summary>
    public void Clear()
    {
        _fullHistory.Clear();
        _summaries.Clear();
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
    /// Переключает режим сжатия.
    /// </summary>
    public void ToggleCompression()
    {
        _config.Enabled = !_config.Enabled;
        var status = _config.Enabled ? "включено" : "выключено";
        _logger.Info($"Управление контекстом: {status}");
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
