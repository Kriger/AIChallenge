using System.Text.Json;
using System.Text.Json.Serialization;
using GigaChatApp.Infrastructure;
using GigaChatApp.Models;

namespace GigaChatApp;

/// <summary>
/// Сериализуемая версия истории диалога.
/// </summary>
internal class HistoryDto
{
    [JsonPropertyName("messages")]
    public List<ApiMessage> Messages { get; set; } = new();
}

/// <summary>
/// Персистентность контекста агента.
/// Разделяет сохранение по отдельным файлам для каждого типа данных:
/// - dialog.json        — история сообщений
/// - short_term.json    — краткосрочная память (диалог)
/// - working.json       — рабочая память (текущая задача)
/// - long_term.json     — долгосрочная память (факты)
/// - cache.json         — кэш ответов
/// - metrics.json       — метрики агента
/// </summary>
public static class ContextPersistence
{
    private const string HistoryFileName = "dialog.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Сохраняет весь контекст агента в отдельные файлы.
    /// </summary>
    public static void SaveContext(ChatAgent agent)
    {
        // 1. История диалога
        SaveHistory(agent);

        // 2. Краткосрочная память
        ShortTermPersistence.Save(agent.MemoryManager.ShortTerm);

        // 3. Рабочая память
        WorkingMemoryPersistence.Save(agent.MemoryManager.Working);

        // 4. Долгосрочная память
        LongTermPersistence.Save(agent.MemoryManager.LongTerm);

        // 5. Кэш
        CachePersistence.Save(agent.Cache);

        // 6. Метрики
        MetricsPersistence.Save(agent.Metrics);

        var msgCount = agent.History.Count;
        var shortTermCount = agent.MemoryManager.ShortTerm.Count;
        var workingCount = agent.MemoryManager.Working.Count;
        var longTermCount = agent.MemoryManager.LongTerm.Count;
        var cacheCount = agent.Cache.Count;

        agent.Logger.Info($"Контекст сохранён: {msgCount} сообщ., {shortTermCount} кратк., {workingCount} рабоч., {longTermCount} фактов, {cacheCount} кэш");
    }

    /// <summary>
    /// Загружает весь контекст агента из отдельных файлов.
    /// </summary>
    public static bool LoadContext(ChatAgent agent)
    {
        bool loaded = false;

        // 1. История диалога
        if (LoadHistory(agent))
            loaded = true;

        // 2. Краткосрочная память
        ShortTermPersistence.Load(agent.MemoryManager.ShortTerm);
        if (agent.MemoryManager.ShortTerm.Count > 0)
            loaded = true;

        // 3. Рабочая память
        WorkingMemoryPersistence.Load(agent.MemoryManager.Working);
        if (agent.MemoryManager.Working.Count > 0)
            loaded = true;

        // 4. Долгосрочная память
        LongTermPersistence.Load(agent.MemoryManager.LongTerm);
        if (agent.MemoryManager.LongTerm.Count > 0)
            loaded = true;

        // 5. Кэш
        CachePersistence.Load(agent.Cache);
        if (agent.Cache.Count > 0)
            loaded = true;

        // 6. Метрики
        MetricsPersistence.Load(agent.Metrics);
        if (agent.Metrics.TotalRequests > 0)
            loaded = true;

        return loaded;
    }

    private static void SaveHistory(ChatAgent agent)
    {
        var dto = new HistoryDto
        {
            Messages = agent.History.ToList(),
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        File.WriteAllText(HistoryFileName, json, Encoding.UTF8);
    }

    private static bool LoadHistory(ChatAgent agent)
    {
        if (!File.Exists(HistoryFileName))
            return false;

        try
        {
            var json = File.ReadAllText(HistoryFileName, Encoding.UTF8);
            var dto = JsonSerializer.Deserialize<HistoryDto>(json, JsonOptions);

            if (dto is null || dto.Messages.Count == 0)
                return false;

            agent.LoadHistory(dto.Messages);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
