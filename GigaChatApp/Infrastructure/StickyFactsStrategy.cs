using GigaChatApp.Models;
using GigaChatApp.Services;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Стратегия 2: Sticky Facts / Key-Value Memory.
/// Хранит отдельный блок "facts" (ключ-значение), который обновляется после каждого сообщения.
/// В запрос отправляется: facts + последние N сообщений.
/// </summary>
public class StickyFactsStrategy : IContextStrategy
{
    private readonly ChatClient _chatClient;
    private readonly AuthClient _authClient;
    private readonly AgentLogger _logger;

    private readonly List<ApiMessage> _messages = new();
    private readonly Dictionary<string, string> _facts = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _windowSize;
    private readonly int _maxFacts;

    /// <summary>Промпт для извлечения фактов из диалога.</summary>
    public string FactExtractionPrompt { get; set; } =
        """
        Извлеки важные факты из следующего диалога.
        Факты — это конкретная информация, которая может пригодиться в будущем:
        - Цель пользователя
        - Ограничения и требования
        - Предпочтения и стили
        - Принятые решения и договорённости
        - Важные данные (имена, числа, ссылки)

        Формат: один факт на строку, "ключ: значение"
        Не извлекай приветствия, вежливые фразы или общие фразы.
        Если фактов нет — напиши "нет фактов".

        Диалог:
        {dialogue}

        Извлеки факты:
        """;

    /// <summary>
    /// Общее количество добавленных сообщений.
    /// </summary>
    public int TotalAdded { get; private set; }

    /// <summary>
    /// Количество обновлений фактов.
    /// </summary>
    public int FactsUpdateCount { get; private set; }

    public string Name => "Sticky Facts";

    public string Description => $"Блок фактов ({_facts.Count}) + последние {_windowSize} сообщений. Факты обновляются LLM после каждого сообщения.";

    /// <summary>
    /// Размер окна — количество последних сообщений.
    /// </summary>
    public int WindowSize => _windowSize;

    /// <summary>
    /// Максимальное количество фактов.
    /// </summary>
    public int MaxFacts => _maxFacts;

    public StickyFactsStrategy(
        ChatClient chatClient,
        AuthClient authClient,
        AgentLogger logger,
        int windowSize = 10,
        int maxFacts = 50)
    {
        _chatClient = chatClient;
        _authClient = authClient;
        _logger = logger;
        _windowSize = Math.Max(1, windowSize);
        _maxFacts = Math.Max(10, maxFacts);
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
        }

        // Если это сообщение пользователя — обновляем факты
        if (message.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            UpdateFactsAsync(message.Content);
        }
    }

    /// <summary>
    /// Принудительно обновить факты на основе текущего диалога.
    /// </summary>
    public void UpdateFacts()
    {
        if (_messages.Count == 0) return;

        var dialogueText = string.Join("\n", _messages.Take(20).Select(m =>
            $"{(m.Role == "user" ? "Пользователь" : "Ассистент")}: {m.Content}"));

        var prompt = FactExtractionPrompt.Replace("{dialogue}", dialogueText);

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

        var token = _authClient.GetAccessTokenAsync().GetAwaiter().GetResult();

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        httpClient.BaseAddress = new Uri("https://api.giga.chat");

        try
        {
            var response = httpClient.PostAsJsonAsync("/v1/chat/completions", requestObj).GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode)
            {
                var parsed = response.Content.ReadFromJsonAsync<Services.CompletionResponse>().GetAwaiter().GetResult();
                if (parsed?.Choices?.Count > 0)
                {
                    var factsText = parsed.Choices[0].Message?.Content ?? "";

                    if (!string.IsNullOrWhiteSpace(factsText) && factsText != "нет фактов")
                    {
                        ParseAndApplyFacts(factsText);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"StickyFacts: ошибка обновления фактов: {ex.Message}");
        }
        finally
        {
            httpClient.Dispose();
        }
    }

    private async void UpdateFactsAsync(string userMessage)
    {
        try
        {
            // Берём последние 20 сообщений для контекста
            var recentMessages = _messages.TakeLast(20);
            var dialogueText = string.Join("\n", recentMessages.Select(m =>
                $"{(m.Role == "user" ? "Пользователь" : "Ассистент")}: {m.Content}"));

            var prompt = FactExtractionPrompt.Replace("{dialogue}", dialogueText);

            var token = await _authClient.GetAccessTokenAsync();

            var extractionMessages = new List<ApiMessage>
            {
                new() { Role = "user", Content = prompt },
            };

            var requestObj = new Dictionary<string, object>
            {
                ["model"] = "GigaChat-2",
                ["messages"] = extractionMessages.Select(m => new { m.Role, m.Content }).ToList<object>(),
                ["stream"] = false,
                ["temperature"] = 0.1,
            };

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            httpClient.BaseAddress = new Uri("https://api.giga.chat");

            var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", requestObj);
            httpClient.Dispose();

            if (response.IsSuccessStatusCode)
            {
                var parsed = await response.Content.ReadFromJsonAsync<Services.CompletionResponse>();
                if (parsed?.Choices?.Count > 0)
                {
                    var factsText = parsed.Choices[0].Message?.Content ?? "";

                    if (!string.IsNullOrWhiteSpace(factsText) && factsText != "нет фактов")
                    {
                        ParseAndApplyFacts(factsText);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"StickyFacts: ошибка обновления фактов: {ex.Message}");
        }
    }

    private void ParseAndApplyFacts(string factsText)
    {
        var lines = factsText.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var newFacts = 0;
        var updatedFacts = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex > 0 && colonIndex < trimmed.Length - 1)
            {
                var key = trimmed[..colonIndex].Trim();
                var value = trimmed[(colonIndex + 1)..].Trim();

                if (key.Length > 0 && value.Length > 0)
                {
                    if (_facts.ContainsKey(key))
                    {
                        _facts[key] = value;
                        updatedFacts++;
                    }
                    else if (_facts.Count < _maxFacts)
                    {
                        _facts[key] = value;
                        newFacts++;
                    }
                }
            }
        }

        if (newFacts > 0 || updatedFacts > 0)
        {
            FactsUpdateCount++;
            _logger.Info($"StickyFacts: +{newFacts} новых, {updatedFacts} обновлённых фактов (всего: {_facts.Count})");
        }
    }

    public ContextResult BuildContext()
    {
        // Формируем facts-блок
        var factsText = BuildFactsText();

        // Собираем system-сообщение с фактами
        var systemMessages = new List<ApiMessage>();
        if (!string.IsNullOrEmpty(factsText))
        {
            systemMessages.Add(new ApiMessage
            {
                Role = "system",
                Content = $"=== ЗАПОМНЕННЫЕ ФАКТЫ ===\n{factsText}\n===========================",
            });
        }

        var originalTokens = TokenEstimator.EstimateHistoryTokens(_messages);
        var processedTokens = TokenEstimator.EstimateHistoryTokens(_messages) + TokenEstimator.EstimateTokens(factsText);

        return new ContextResult
        {
            Messages = _messages.ToList(),
            SystemMessages = systemMessages,
            SummaryText = factsText,
            OriginalTokens = originalTokens,
            CompressedTokens = processedTokens,
            Description = $"Sticky Facts: {_facts.Count} фактов, {_messages.Count} сообщений",
            IsCompressed = _messages.Count >= _windowSize,
        };
    }

    private string BuildFactsText()
    {
        if (_facts.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var kvp in _facts)
        {
            sb.AppendLine($"{kvp.Key}: {kvp.Value}");
        }
        return sb.ToString().TrimEnd();
    }

    public void Clear()
    {
        _messages.Clear();
        _facts.Clear();
        TotalAdded = 0;
        FactsUpdateCount = 0;
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
        }
    }

    /// <summary>
    /// Добавить факт вручную.
    /// </summary>
    public void SaveFact(string key, string value)
    {
        if (_facts.ContainsKey(key))
        {
            _facts[key] = value;
        }
        else if (_facts.Count < _maxFacts)
        {
            _facts[key] = value;
        }
        else
        {
            // Удаляем самый старый факт (по алфавиту как простой LRU)
            var oldestKey = _facts.Keys.OrderBy(k => k).FirstOrDefault();
            if (oldestKey is not null)
            {
                _facts.Remove(oldestKey);
            }
            _facts[key] = value;
        }
    }

    /// <summary>
    /// Удалить факт по ключу.
    /// </summary>
    public bool DeleteFact(string key)
    {
        return _facts.Remove(key);
    }

    /// <summary>
    /// Получить все факты.
    /// </summary>
    public IReadOnlyDictionary<string, string> Facts => _facts;

    public string GetStatus()
    {
        return $"""
            Стратегия: {Name}
            Размер окна: {_windowSize}
            Максимум фактов: {_maxFacts}
            Текущих сообщений: {_messages.Count}
            Фактов: {_facts.Count}
            Всего добавлено: {TotalAdded}
            Обновлений фактов: {FactsUpdateCount}
            """;
    }
}
