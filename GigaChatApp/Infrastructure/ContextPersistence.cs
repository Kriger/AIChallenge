using System.Text.Json;
using System.Text.Json.Serialization;
using GigaChatApp.Infrastructure;
using GigaChatApp.Models;

namespace GigaChatApp;

/// <summary>
/// Сериализуемая версия CachedEntry для JSON.
/// </summary>
internal class CachedEntryDto
{
    [JsonPropertyName("answer")]
    public string Answer { get; set; } = string.Empty;

    [JsonPropertyName("addedAt")]
    public DateTime AddedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Сериализуемая версия факта для JSON.
/// </summary>
internal class FactDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "user";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastReadAt")]
    public DateTime LastReadAt { get; set; }

    [JsonPropertyName("readCount")]
    public int ReadCount { get; set; }
}

/// <summary>
/// Сериализуемая версия метрик для JSON.
/// </summary>
internal class MetricsDto
{
    [JsonPropertyName("totalRequests")]
    public int TotalRequests { get; set; }

    [JsonPropertyName("successfulRequests")]
    public int SuccessfulRequests { get; set; }

    [JsonPropertyName("failedRequests")]
    public int FailedRequests { get; set; }

    [JsonPropertyName("cachedRequests")]
    public int CachedRequests { get; set; }

    [JsonPropertyName("retryAttempts")]
    public int RetryAttempts { get; set; }

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }

    [JsonPropertyName("retryDelayMs")]
    public int RetryDelayMs { get; set; }

    [JsonPropertyName("totalDurationTicks")]
    public long TotalDurationTicks { get; set; }

    [JsonPropertyName("totalPromptTokens")]
    public long TotalPromptTokens { get; set; }

    [JsonPropertyName("totalCompletionTokens")]
    public long TotalCompletionTokens { get; set; }
}

/// <summary>
/// Сериализуемая версия истории диалога.
/// </summary>
internal class HistoryDto
{
    [JsonPropertyName("messages")]
    public List<ApiMessage> Messages { get; set; } = new();
}

/// <summary>
/// Полный контекст агента для сохранения и восстановления.
/// </summary>
internal class AgentContext
{
    [JsonPropertyName("version")]
    public int Version => 1;

    [JsonPropertyName("savedAt")]
    public DateTime SavedAt { get; set; }

    [JsonPropertyName("history")]
    public HistoryDto History { get; set; } = new();

    [JsonPropertyName("memory")]
    public Dictionary<string, FactDto> Memory { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("cache")]
    public Dictionary<string, CachedEntryDto> Cache { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("metrics")]
    public MetricsDto Metrics { get; set; } = new();
}

/// <summary>
/// Сервис сохранения и загрузки контекста агента в JSON-файл.
/// </summary>
public static class ContextPersistence
{
    private const string ContextFileName = "agent_context.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Путь к файлу контекста.
    /// </summary>
    public static string ContextFilePath => Path.GetFullPath(ContextFileName);

    /// <summary>
    /// Сохраняет контекст агента в JSON-файл.
    /// </summary>
    public static void SaveContext(ChatAgent agent)
    {
        var context = new AgentContext
        {
            SavedAt = DateTime.UtcNow,
            History = new HistoryDto
            {
                Messages = agent.History.ToList(),
            },
            Memory = agent.Memory.All.ToDictionary(
                kvp => kvp.Key,
                kvp => new FactDto
                {
                    Key = kvp.Value.Key,
                    Value = kvp.Value.Value,
                    Source = kvp.Value.Source,
                    CreatedAt = kvp.Value.CreatedAt,
                    LastReadAt = kvp.Value.LastReadAt,
                    ReadCount = kvp.Value.ReadCount,
                },
                StringComparer.OrdinalIgnoreCase),
            Cache = new Dictionary<string, CachedEntryDto>(StringComparer.OrdinalIgnoreCase),
            Metrics = new MetricsDto
            {
                TotalRequests = agent.Metrics.TotalRequests,
                SuccessfulRequests = agent.Metrics.SuccessfulRequests,
                FailedRequests = agent.Metrics.FailedRequests,
                CachedRequests = agent.Metrics.CachedRequests,
                RetryAttempts = agent.Metrics.RetryAttempts,
                RetryCount = agent.Metrics.RetryCount,
                RetryDelayMs = agent.Metrics.RetryDelayMs,
                TotalDurationTicks = agent.Metrics.TotalDuration.Ticks,
                TotalPromptTokens = agent.Metrics.TotalPromptTokens,
                TotalCompletionTokens = agent.Metrics.TotalCompletionTokens,
            },
        };

        // Сохраняем кэш через рефлексию (ConcurrentDictionary не имеет публичного доступа к элементам)
        var cacheField = typeof(RequestCache).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (cacheField?.GetValue(agent.Cache) is System.Collections.Concurrent.ConcurrentDictionary<string, CachedEntry> cacheDict)
        {
            foreach (var kvp in cacheDict)
            {
                context.Cache[kvp.Key] = new CachedEntryDto
                {
                    Answer = kvp.Value.Answer,
                    AddedAt = kvp.Value.AddedAt,
                    ExpiresAt = kvp.Value.ExpiresAt,
                };
            }
        }

        var json = JsonSerializer.Serialize(context, JsonOptions);
        File.WriteAllText(ContextFilePath, json, Encoding.UTF8);
        agent.Logger.Info($"Контекст сохранён в {ContextFilePath} ({agent.History.Count} сообщений, {agent.Memory.Count} фактов, {agent.Cache.Count} записей кэша)");
    }

    /// <summary>
    /// Загружает контекст агента из JSON-файла и восстанавливает состояние.
    /// </summary>
    /// <returns>true, если контекст успешно загружен; иначе — false.</returns>
    public static bool LoadContext(ChatAgent agent)
    {
        if (!File.Exists(ContextFilePath))
        {
            agent.Logger.Info("Файл контекста не найден, начинаем новый диалог");
            return false;
        }

        try
        {
            var json = File.ReadAllText(ContextFilePath, Encoding.UTF8);
            var context = JsonSerializer.Deserialize<AgentContext>(json, JsonOptions);

            if (context is null)
            {
                agent.Logger.Warning("Не удалось десериализовать контекст, начинаем новый диалог");
                return false;
            }

            // Восстанавливаем историю диалога
            if (context.History.Messages.Count > 0)
            {
                agent.LoadHistory(context.History.Messages);
            }

            // Восстанавливаем долгосрочную память
            foreach (var kvp in context.Memory)
            {
                agent.Memory.Save(
                    kvp.Value.Key,
                    kvp.Value.Value,
                    kvp.Value.Source);

                // Восстанавливаем метрики чтения
                if (agent.Memory.All.TryGetValue(kvp.Key, out var fact))
                {
                    fact.LastReadAt = kvp.Value.LastReadAt;
                    fact.ReadCount = kvp.Value.ReadCount;
                }
            }

            agent.Logger.Info($"Восстановлена память: {agent.Memory.Count} фактов");

            // Восстанавливаем метрики
            agent.Metrics.TotalRequests = context.Metrics.TotalRequests;
            agent.Metrics.SuccessfulRequests = context.Metrics.SuccessfulRequests;
            agent.Metrics.FailedRequests = context.Metrics.FailedRequests;
            agent.Metrics.CachedRequests = context.Metrics.CachedRequests;
            agent.Metrics.RetryAttempts = context.Metrics.RetryAttempts;
            agent.Metrics.RetryCount = context.Metrics.RetryCount;
            agent.Metrics.RetryDelayMs = context.Metrics.RetryDelayMs;
            agent.Metrics.TotalDuration = TimeSpan.FromTicks(context.Metrics.TotalDurationTicks);
            agent.Metrics.TotalPromptTokens = context.Metrics.TotalPromptTokens;
            agent.Metrics.TotalCompletionTokens = context.Metrics.TotalCompletionTokens;

            agent.Logger.Info($"Восстановлены метрики: {agent.Metrics.TotalRequests} запросов, success rate {agent.Metrics.SuccessRate:P1}");

            // Восстанавливаем кэш (просроченные записи не сохраняем)
            var validEntries = context.Cache
                .Where(kvp => kvp.Value.ExpiresAt > DateTime.UtcNow)
                .Select(kvp => new CachedEntry
                {
                    Answer = kvp.Value.Answer,
                    AddedAt = kvp.Value.AddedAt,
                    ExpiresAt = kvp.Value.ExpiresAt,
                });

            agent.Cache.LoadEntries(validEntries);
            agent.Logger.Info($"Восстановлен кэш: {agent.Cache.Count} записей");
            agent.Logger.Info($"Контекст загружен (сохранён: {context.SavedAt:yyyy-MM-dd HH:mm:ss} UTC)");

            return true;
        }
        catch (Exception ex)
        {
            agent.Logger.Warning($"Ошибка загрузки контекста: {ex.Message}, начинаем новый диалог");
            return false;
        }
    }
}
