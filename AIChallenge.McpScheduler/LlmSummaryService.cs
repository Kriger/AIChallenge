using System.Text.Json;

namespace AIChallenge.McpScheduler;

/// <summary>
/// Сервис для генерации LLM-summary на основе данных задач.
/// </summary>
public sealed class LlmSummaryService : IDisposable
{
    private readonly GigaChatClient _client;
    private readonly Action<string> _log;
    private readonly string _model;
    private readonly double _temperature;
    private readonly int _maxTokens;

    public LlmSummaryService(string clientId, string clientSecret, string model, double temperature, int maxTokens, Action<string> log)
    {
        _client = new GigaChatClient(clientId, clientSecret);
        _log = log;
        _model = model;
        _temperature = temperature;
        _maxTokens = maxTokens;
    }

    /// <summary>
    /// Генерирует умный summary на основе сырых данных задач.
    /// </summary>
    public async Task<string> GenerateSummaryAsync(string rawTasksJson)
    {
        var cleanData = FilterJson(rawTasksJson);
        _log("🤖 Отправляю данные в GigaChat для анализа...");

        var systemPrompt = @"Ты — аналитик задач. Твоя задача — проанализировать данные и составить краткий, осмысленный отчёт на русском языке.

Правила:
- Пиши кратко, по делу, без воды
- Выделяй главное: что выполнено, что в работе, на что обратить внимание
- Используй эмодзи для навигации
- Если всё хорошо — скажи об этом
- Если есть проблемы — подсвети их
- Не перечисляй все задачи, только ключевые моменты";

        var userPrompt = $"""
            Проанализируй данные по задачам и составь краткий отчёт:

            {cleanData}

            Отвечай на русском языке, кратко и по делу.
            """;

        var result = await _client.ChatAsync(_model, systemPrompt, userPrompt, _temperature, _maxTokens);
        return result.Trim();
    }

    /// <summary>
    /// Генерирует summary с учётом предыдущего отчёта.
    /// </summary>
    public async Task<string> GenerateDiffSummaryAsync(string rawTasksJson, string? previousSummary)
    {
        var cleanData = FilterJson(rawTasksJson);
        _log("🤖 Отправляю данные в GigaChat для анализа изменений...");

        var systemPrompt = @"Ты — аналитик задач. Тебе даны текущие данные и предыдущий отчёт. Составь отчёт об изменениях.

Правила:
- Сравни с предыдущим состоянием
- Подсвети что изменилось: сколько задач выполнено, сколько новых
- Укажи на проблемы и риски
- Пиши кратко, по делу, на русском
- Используй эмодзи для навигации";

        var userPrompt = previousSummary is null
            ? $"""
                Составь первый отчёт по задачам:

                {cleanData}
                """
            : $"""
                Предыдущий отчёт:
                {previousSummary}

                Текущие данные:
                {cleanData}

                Что изменилось? Что нужно знать?
                """;

        var result = await _client.ChatAsync(_model, systemPrompt, userPrompt, _temperature, _maxTokens);
        return result.Trim();
    }

    /// <summary>
    /// Фильтрует JSON, оставляя только Title и Status.
    /// </summary>
    private static string FilterJson(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                var filtered = new List<string>();
                foreach (var item in root.EnumerateArray())
                {
                    var title = ExtractString(item, "Title", "title", "Название", "name");
                    var status = ExtractString(item, "Status", "status", "Статус", "IsCompleted", "isCompleted");

                    var sb = new StringBuilder();
                    sb.Append("{");
                    if (!string.IsNullOrEmpty(title))
                        sb.Append($"\"Title\":\"{title}\",");
                    if (!string.IsNullOrEmpty(status))
                        sb.Append($"\"Status\":\"{status}\"");
                    sb.Append("}");

                    filtered.Add(sb.ToString());
                }
                return $"[{string.Join(",", filtered)}]";
            }
        }
        catch
        {
            // Если не удалось распарсить — возвращаем оригинал
        }
        return rawJson;
    }

    private static string? ExtractString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                return prop.ValueKind switch
                {
                    JsonValueKind.String => prop.GetString(),
                    JsonValueKind.Number => prop.GetInt32().ToString(),
                    JsonValueKind.True or JsonValueKind.False => prop.GetBoolean().ToString().ToLowerInvariant(),
                    JsonValueKind.Null => null,
                    _ => prop.ToString()
                };
            }
        }
        return null;
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
