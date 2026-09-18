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

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 1;

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

    [JsonPropertyName("totalContextTokens")]
    public long TotalContextTokens { get; set; }

    [JsonPropertyName("lastContextTokens")]
    public long LastContextTokens { get; set; }

    [JsonPropertyName("contextCompressionEnabled")]
    public bool ContextCompressionEnabled { get; set; }

    [JsonPropertyName("contextComparisonCount")]
    public int ContextComparisonCount { get; set; }

    [JsonPropertyName("contextTotalOriginalTokens")]
    public long ContextTotalOriginalTokens { get; set; }

    [JsonPropertyName("contextTotalCompressedTokens")]
    public long ContextTotalCompressedTokens { get; set; }

    [JsonPropertyName("contextTotalReplacedMessages")]
    public long ContextTotalReplacedMessages { get; set; }

    [JsonPropertyName("contextTotalSentMessages")]
    public long ContextTotalSentMessages { get; set; }

    [JsonPropertyName("contextMaxTokenSavings")]
    public long ContextMaxTokenSavings { get; set; }
}

/// <summary>
/// Сериализуемая версия summary блока для JSON.
/// </summary>
internal class SummaryBlockDto
{
    [JsonPropertyName("blockNumber")]
    public int BlockNumber { get; set; }

    [JsonPropertyName("timeRange")]
    public string TimeRange { get; set; } = string.Empty;

    [JsonPropertyName("replacedCount")]
    public int ReplacedCount { get; set; }

    [JsonPropertyName("summaryText")]
    public string SummaryText { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Сериализуемая версия конфигурации контекста для JSON.
/// </summary>
internal class ContextConfigDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("recentMessageCount")]
    public int RecentMessageCount { get; set; }

    [JsonPropertyName("summaryInterval")]
    public int SummaryInterval { get; set; }

    [JsonPropertyName("maxSummaries")]
    public int MaxSummaries { get; set; }

    [JsonPropertyName("maxContextTokens")]
    public int MaxContextTokens { get; set; }

    [JsonPropertyName("strategy")]
    public string Strategy { get; set; } = "SlidingWindow";
}

/// <summary>
/// Сериализуемая версия ветки диалога для JSON.
/// </summary>
internal class DialogueBranchDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ApiMessage> Messages { get; set; } = new();

    [JsonPropertyName("facts")]
    public Dictionary<string, string> Facts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; }

    [JsonPropertyName("isMain")]
    public bool IsMain { get; set; }
}

/// <summary>
/// Сериализуемая версия чекпоинта для JSON.
/// </summary>
internal class BranchCheckpointDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("messageIndex")]
    public int MessageIndex { get; set; }

    [JsonPropertyName("branchId")]
    public string BranchId { get; set; } = string.Empty;

    [JsonPropertyName("facts")]
    public Dictionary<string, string> Facts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("messageCount")]
    public int MessageCount { get; set; }
}

/// <summary>
/// Сериализуемая версия ShortTermMemory для JSON.
/// </summary>
internal class ShortTermMemoryDto
{
    [JsonPropertyName("entries")]
    public List<ShortTermEntryDto> Entries { get; set; } = new();

    [JsonPropertyName("maxSize")]
    public int MaxSize { get; set; } = 100;
}

internal class ShortTermEntryDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public string? Metadata { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Сериализуемая версия WorkingMemory для JSON.
/// </summary>
internal class WorkingMemoryDto
{
    [JsonPropertyName("entries")]
    public Dictionary<string, WorkingEntryDto> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("currentTaskId")]
    public string? CurrentTaskId { get; set; }

    [JsonPropertyName("currentTaskStatus")]
    public string? CurrentTaskStatus { get; set; }

    [JsonPropertyName("taskStartedAt")]
    public DateTime? TaskStartedAt { get; set; }

    [JsonPropertyName("archiveEnabled")]
    public bool ArchiveEnabled { get; set; } = true;

    [JsonPropertyName("maxArchiveSize")]
    public int MaxArchiveSize { get; set; } = 10;

    [JsonPropertyName("archivedTasks")]
    public Dictionary<string, List<WorkingEntryDto>> ArchivedTasks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal class WorkingEntryDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "data";

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Сериализуемая версия фактов StickyFacts для JSON.
/// </summary>
internal class StickyFactsDto
{
    [JsonPropertyName("facts")]
    public Dictionary<string, string> Facts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("totalAdded")]
    public int TotalAdded { get; set; }

    [JsonPropertyName("factsUpdateCount")]
    public int FactsUpdateCount { get; set; }
}

/// <summary>
/// Сериализуемая версия SlidingWindow для JSON.
/// </summary>
internal class SlidingWindowDto
{
    [JsonPropertyName("windowSize")]
    public int WindowSize { get; set; }

    [JsonPropertyName("totalAdded")]
    public int TotalAdded { get; set; }

    [JsonPropertyName("droppedCount")]
    public int DroppedCount { get; set; }
}

/// <summary>
/// Сериализуемая версия Branching для JSON.
/// </summary>
internal class BranchingDto
{
    [JsonPropertyName("activeBranchId")]
    public string ActiveBranchId { get; set; } = string.Empty;

    [JsonPropertyName("branches")]
    public List<DialogueBranchDto> Branches { get; set; } = new();

    [JsonPropertyName("checkpoints")]
    public List<BranchCheckpointDto> Checkpoints { get; set; } = new();

    [JsonPropertyName("branchCounter")]
    public int BranchCounter { get; set; }

    [JsonPropertyName("checkpointCounter")]
    public int CheckpointCounter { get; set; }
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
    public int Version => 2;

    [JsonPropertyName("savedAt")]
    public DateTime SavedAt { get; set; }

    [JsonPropertyName("history")]
    public HistoryDto History { get; set; } = new();

    [JsonPropertyName("shortTermMemory")]
    public ShortTermMemoryDto ShortTermMemory { get; set; } = new();

    [JsonPropertyName("workingMemory")]
    public WorkingMemoryDto WorkingMemory { get; set; } = new();

    [JsonPropertyName("memory")]
    public Dictionary<string, FactDto> Memory { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("cache")]
    public Dictionary<string, CachedEntryDto> Cache { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("metrics")]
    public MetricsDto Metrics { get; set; } = new();

    [JsonPropertyName("contextManager")]
    public ContextManagerDto ContextManager { get; set; } = new();
}

/// <summary>
/// Сериализуемая версия ContextManager для JSON.
/// </summary>
internal class ContextManagerDto
{
    [JsonPropertyName("config")]
    public ContextConfigDto Config { get; set; } = new();

    [JsonPropertyName("summaries")]
    public List<SummaryBlockDto> Summaries { get; set; } = new();

    [JsonPropertyName("comparisonMetrics")]
    public ContextComparisonMetricsDto ComparisonMetrics { get; set; } = new();

    [JsonPropertyName("slidingWindow")]
    public SlidingWindowDto? SlidingWindow { get; set; }

    [JsonPropertyName("stickyFacts")]
    public StickyFactsDto? StickyFacts { get; set; }

    [JsonPropertyName("branching")]
    public BranchingDto? Branching { get; set; }
}

/// <summary>
/// Сериализуемая версия метрик сравнения контекста для JSON.
/// </summary>
internal class ContextComparisonMetricsDto
{
    [JsonPropertyName("comparisonCount")]
    public int ComparisonCount { get; set; }

    [JsonPropertyName("totalOriginalTokens")]
    public long TotalOriginalTokens { get; set; }

    [JsonPropertyName("totalCompressedTokens")]
    public long TotalCompressedTokens { get; set; }

    [JsonPropertyName("totalReplacedMessages")]
    public long TotalReplacedMessages { get; set; }

    [JsonPropertyName("totalSentMessages")]
    public long TotalSentMessages { get; set; }

    [JsonPropertyName("maxTokenSavings")]
    public long MaxTokenSavings { get; set; }
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
            ShortTermMemory = new ShortTermMemoryDto
            {
                Entries = agent.MemoryManager.ShortTerm.GetAll().Select(e => new ShortTermEntryDto
                {
                    Role = e.Role,
                    Content = e.Content,
                    Metadata = e.Metadata,
                    Timestamp = e.Timestamp,
                }).ToList(),
                MaxSize = agent.MemoryManager.ShortTerm.MaxSize,
            },
            WorkingMemory = new WorkingMemoryDto
            {
                Entries = agent.MemoryManager.Working.All.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new WorkingEntryDto
                    {
                        Key = kvp.Value.Key,
                        Value = kvp.Value.Value,
                        Type = kvp.Value.Type,
                        TaskId = kvp.Value.TaskId,
                        CreatedAt = kvp.Value.CreatedAt,
                        UpdatedAt = kvp.Value.UpdatedAt,
                    },
                    StringComparer.OrdinalIgnoreCase),
                CurrentTaskId = agent.MemoryManager.Working.CurrentTaskId,
                CurrentTaskStatus = agent.MemoryManager.Working.CurrentTaskStatus,
                TaskStartedAt = agent.MemoryManager.Working.TaskStartedAt,
                ArchiveEnabled = agent.MemoryManager.Working.ArchiveEnabled,
                MaxArchiveSize = agent.MemoryManager.Working.MaxArchiveSize,
                ArchivedTasks = agent.MemoryManager.Working.AllArchived.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Select(e => new WorkingEntryDto
                    {
                        Key = e.Key,
                        Value = e.Value,
                        Type = e.Type,
                        TaskId = e.TaskId,
                        CreatedAt = e.CreatedAt,
                        UpdatedAt = e.UpdatedAt,
                    }).ToList(),
                    StringComparer.OrdinalIgnoreCase),
            },
            Memory = agent.Memory.All.ToDictionary(
                kvp => kvp.Key,
                kvp => new FactDto
                {
                    Key = kvp.Value.Key,
                    Value = kvp.Value.Value,
                    Source = kvp.Value.Source,
                    Priority = (int)kvp.Value.Priority,
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
                TotalContextTokens = agent.Metrics.TotalContextTokens,
                LastContextTokens = agent.Metrics.LastContextTokens,
                ContextCompressionEnabled = agent.Metrics.ContextCompressionEnabled,
                ContextComparisonCount = agent.Metrics.ContextComparison.ComparisonCount,
                ContextTotalOriginalTokens = agent.Metrics.ContextComparison.TotalOriginalTokens,
                ContextTotalCompressedTokens = agent.Metrics.ContextComparison.TotalCompressedTokens,
                ContextTotalReplacedMessages = agent.Metrics.ContextComparison.TotalReplacedMessages,
                ContextTotalSentMessages = agent.Metrics.ContextComparison.TotalSentMessages,
                ContextMaxTokenSavings = agent.Metrics.ContextComparison.MaxTokenSavings,
            },
            ContextManager = new ContextManagerDto
            {
                Config = new ContextConfigDto
                {
                    Enabled = agent.ContextManager.Config.Enabled,
                    RecentMessageCount = agent.ContextManager.Config.RecentMessageCount,
                    SummaryInterval = agent.ContextManager.Config.SummaryInterval,
                    MaxSummaries = agent.ContextManager.Config.MaxSummaries,
                    MaxContextTokens = agent.ContextManager.Config.MaxContextTokens,
                    Strategy = agent.ContextManager.Config.Strategy.ToString(),
                },
                Summaries = agent.ContextManager.GetSummaries().Select(s => new SummaryBlockDto
                {
                    BlockNumber = s.BlockNumber,
                    TimeRange = s.TimeRange,
                    ReplacedCount = s.ReplacedCount,
                    SummaryText = s.SummaryText,
                    CreatedAt = s.CreatedAt,
                }).ToList(),
                ComparisonMetrics = new ContextComparisonMetricsDto
                {
                    ComparisonCount = agent.Metrics.ContextComparison.ComparisonCount,
                    TotalOriginalTokens = agent.Metrics.ContextComparison.TotalOriginalTokens,
                    TotalCompressedTokens = agent.Metrics.ContextComparison.TotalCompressedTokens,
                    TotalReplacedMessages = agent.Metrics.ContextComparison.TotalReplacedMessages,
                    TotalSentMessages = agent.Metrics.ContextComparison.TotalSentMessages,
                    MaxTokenSavings = agent.Metrics.ContextComparison.MaxTokenSavings,
                },
                SlidingWindow = agent.ContextManager.SlidingWindow is { } sw ? new SlidingWindowDto
                {
                    WindowSize = 10, // window size is config-dependent
                    TotalAdded = sw.TotalAdded,
                    DroppedCount = sw.DroppedCount,
                } : null,
                StickyFacts = agent.ContextManager.StickyFacts is { } sf ? new StickyFactsDto
                {
                    Facts = sf.Facts.ToDictionary(k => k.Key, v => v.Value),
                    TotalAdded = sf.TotalAdded,
                    FactsUpdateCount = sf.FactsUpdateCount,
                } : null,
                Branching = agent.ContextManager.Branching is { } br ? new BranchingDto
                {
                    ActiveBranchId = br.ActiveBranchId,
                    Branches = br.GetAllBranches().Select(b => new DialogueBranchDto
                    {
                        Id = b.Id,
                        Name = b.Name,
                        Messages = b.Messages,
                        Facts = new Dictionary<string, string>(b.Facts, StringComparer.OrdinalIgnoreCase),
                        CreatedAt = b.CreatedAt,
                        LastModified = b.LastModified,
                        IsMain = b.IsMain,
                    }).ToList(),
                    Checkpoints = br.GetAllCheckpoints().Select(cp => new BranchCheckpointDto
                    {
                        Id = cp.Id,
                        Name = cp.Name,
                        MessageIndex = cp.MessageIndex,
                        BranchId = cp.BranchId,
                        Facts = new Dictionary<string, string>(cp.Facts, StringComparer.OrdinalIgnoreCase),
                        CreatedAt = cp.CreatedAt,
                        MessageCount = cp.MessageCount,
                    }).ToList(),
                    BranchCounter = 0,
                    CheckpointCounter = 0,
                } : null,
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
        agent.Logger.Info($"Контекст сохранён в {ContextFilePath} ({agent.History.Count} сообщений, {agent.MemoryManager.ShortTerm.Count} кратк., {agent.MemoryManager.Working.Count} рабоч., {agent.Memory.Count} фактов, {agent.Cache.Count} записей кэша)");
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

            // Восстанавливаем краткосрочную память
            foreach (var entry in context.ShortTermMemory.Entries)
            {
                agent.MemoryManager.ShortTerm.Add(entry.Role, entry.Content, entry.Metadata);
            }
            agent.MemoryManager.ShortTerm.MaxSize = context.ShortTermMemory.MaxSize;
            agent.Logger.Info($"Восстановлена краткосрочная память: {agent.MemoryManager.ShortTerm.Count} записей");

            // Восстанавливаем рабочую память
            foreach (var kvp in context.WorkingMemory.Entries)
            {
                var entry = kvp.Value;
                agent.MemoryManager.Working.Save(entry.Key, entry.Value, entry.Type);
                if (agent.MemoryManager.Working.All.TryGetValue(kvp.Key, out var workingEntry))
                {
                    workingEntry.TaskId = entry.TaskId;
                    workingEntry.CreatedAt = entry.CreatedAt;
                    workingEntry.UpdatedAt = entry.UpdatedAt;
                }
            }
            agent.MemoryManager.Working.CurrentTaskId = context.WorkingMemory.CurrentTaskId;
            agent.MemoryManager.Working.CurrentTaskStatus = context.WorkingMemory.CurrentTaskStatus;
            agent.MemoryManager.Working.TaskStartedAt = context.WorkingMemory.TaskStartedAt;
            agent.MemoryManager.Working.ArchiveEnabled = context.WorkingMemory.ArchiveEnabled;
            agent.MemoryManager.Working.MaxArchiveSize = context.WorkingMemory.MaxArchiveSize;

            // Восстанавливаем архив
            foreach (var kvp in context.WorkingMemory.ArchivedTasks)
            {
                foreach (var entry in kvp.Value)
                {
                    var we = new WorkingEntry
                    {
                        Key = entry.Key,
                        Value = entry.Value,
                        Type = entry.Type,
                        TaskId = entry.TaskId,
                        CreatedAt = entry.CreatedAt,
                        UpdatedAt = entry.UpdatedAt,
                    };
                    if (!context.WorkingMemory.ArchivedTasks.ContainsKey(kvp.Key))
                    {
                        context.WorkingMemory.ArchivedTasks[kvp.Key] = new List<WorkingEntryDto>();
                    }
                }
                // Восстанавливаем через рефлексию, т.к. _archivedTasks приватный
                var archivedField = typeof(WorkingMemory).GetField("_archivedTasks",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (archivedField?.GetValue(agent.MemoryManager.Working) is Dictionary<string, List<WorkingEntry>> archivedDict)
                {
                    foreach (var archKvp in kvp.Value)
                    {
                        var archEntry = new WorkingEntry
                        {
                            Key = archKvp.Key,
                            Value = archKvp.Value,
                            Type = archKvp.Type,
                            TaskId = archKvp.TaskId,
                            CreatedAt = archKvp.CreatedAt,
                            UpdatedAt = archKvp.UpdatedAt,
                        };
                        if (!archivedDict.ContainsKey(kvp.Key))
                        {
                            archivedDict[kvp.Key] = new List<WorkingEntry>();
                        }
                        archivedDict[kvp.Key].Add(archEntry);
                    }
                }
            }

            agent.Logger.Info($"Восстановлена рабочая память: {agent.MemoryManager.Working.Count} записей, {agent.MemoryManager.Working.ArchivedTaskCount} архивных задач");

            // Восстанавливаем долгосрочную память
            foreach (var kvp in context.Memory)
            {
                var priority = (FactPriority)kvp.Value.Priority;
                agent.Memory.Save(
                    kvp.Value.Key,
                    kvp.Value.Value,
                    kvp.Value.Source,
                    priority);

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

            // Восстанавливаем состояние ContextManager
            if (context.ContextManager is { Config: not null })
            {
                var cmConfig = context.ContextManager.Config;
                agent.ContextManager.Config.Enabled = cmConfig.Enabled;
                agent.ContextManager.Config.RecentMessageCount = cmConfig.RecentMessageCount;
                agent.ContextManager.Config.SummaryInterval = cmConfig.SummaryInterval;
                agent.ContextManager.Config.MaxSummaries = cmConfig.MaxSummaries;
                agent.ContextManager.Config.MaxContextTokens = cmConfig.MaxContextTokens;

                // Восстанавливаем стратегию
                if (Enum.TryParse(cmConfig.Strategy, ignoreCase: true, out ContextStrategy strategy))
                {
                    agent.ContextManager.SetStrategy(strategy);
                    agent.Logger.Info($"Восстановлена стратегия контекста: {strategy}");
                }

                // Восстанавливаем полную историю (те же сообщения, что и в agent.History)
                if (context.History.Messages is { Count: > 0 })
                {
                    agent.ContextManager.LoadHistory(context.History.Messages);
                }

                // Восстанавливаем summary блоки
                if (context.ContextManager.Summaries is { Count: > 0 })
                {
                    foreach (var summaryDto in context.ContextManager.Summaries)
                    {
                        agent.ContextManager.AddSummaryBlock(new ContextManager.SummaryBlock
                        {
                            BlockNumber = summaryDto.BlockNumber,
                            TimeRange = summaryDto.TimeRange,
                            ReplacedCount = summaryDto.ReplacedCount,
                            SummaryText = summaryDto.SummaryText,
                            CreatedAt = summaryDto.CreatedAt,
                        });
                    }
                    agent.Logger.Info($"Восстановлено {context.ContextManager.Summaries.Count} summary блоков");
                }

                // Восстанавливаем метрики сравнения
                var compMetrics = context.ContextManager.ComparisonMetrics;
                agent.Metrics.ContextComparison = new ContextComparisonMetrics
                {
                    ComparisonCount = compMetrics.ComparisonCount,
                    TotalOriginalTokens = compMetrics.TotalOriginalTokens,
                    TotalCompressedTokens = compMetrics.TotalCompressedTokens,
                    TotalReplacedMessages = compMetrics.TotalReplacedMessages,
                    TotalSentMessages = compMetrics.TotalSentMessages,
                    MaxTokenSavings = compMetrics.MaxTokenSavings,
                };
                agent.Logger.Info($"Восстановлены метрики сравнения: {compMetrics.ComparisonCount} сравнений");

                // Восстанавливаем Branching (ветки и чекпоинты)
                if (context.ContextManager.Branching is { Branches: not null } branchingDto)
                {
                    var branching = agent.ContextManager.Branching;
                    if (branching is not null)
                    {
                        // Очищаем текущие ветки и чекпоинты
                        branching.Clear();

                        // Восстанавливаем ветки
                        foreach (var branchDto in branchingDto.Branches)
                        {
                            var branch = new DialogueBranch
                            {
                                Id = branchDto.Id,
                                Name = branchDto.Name,
                                Messages = branchDto.Messages,
                                Facts = new Dictionary<string, string>(branchDto.Facts, StringComparer.OrdinalIgnoreCase),
                                CreatedAt = branchDto.CreatedAt,
                                LastModified = branchDto.LastModified,
                                IsMain = branchDto.IsMain,
                            };
                            branching.AddBranchInternal(branch);
                        }

                        // Восстанавливаем чекпоинты
                        foreach (var cpDto in branchingDto.Checkpoints)
                        {
                            var cp = new BranchCheckpoint
                            {
                                Id = cpDto.Id,
                                Name = cpDto.Name,
                                MessageIndex = cpDto.MessageIndex,
                                BranchId = cpDto.BranchId,
                                Facts = new Dictionary<string, string>(cpDto.Facts, StringComparer.OrdinalIgnoreCase),
                                CreatedAt = cpDto.CreatedAt,
                                MessageCount = cpDto.MessageCount,
                            };
                            branching.AddCheckpointInternal(cp);
                        }

                        // Восстанавливаем активную ветку
                        if (!string.IsNullOrEmpty(branchingDto.ActiveBranchId))
                        {
                            branching.ActiveBranchId = branchingDto.ActiveBranchId;
                        }

                        agent.Logger.Info($"Восстановлено {branchingDto.Branches.Count} веток и {branchingDto.Checkpoints.Count} чекпоинтов");
                    }
                }

                // Восстанавливаем StickyFacts
                if (context.ContextManager.StickyFacts is { Facts: not null } factsDto)
                {
                    var stickyFacts = agent.ContextManager.StickyFacts;
                    if (stickyFacts is not null)
                    {
                        foreach (var kvp in factsDto.Facts)
                        {
                            stickyFacts.SaveFact(kvp.Key, kvp.Value);
                        }
                        agent.Logger.Info($"Восстановлено {factsDto.Facts.Count} фактов StickyFacts");
                    }
                }

                // === Миграция: переносим факты из ветки/StickyFacts в LongTermMemory ===
                // Старые контексты сохраняли факты только в Branching/StickyFacts,
                // а не в LongTermMemory. Мигрируем их при загрузке.
                var migratedCount = 0;

                // Мигрируем факты из восстановленных веток
                if (agent.ContextManager.Branching is { } br)
                {
                    foreach (var branch in br.GetAllBranches())
                    {
                        foreach (var kvp in branch.Facts)
                        {
                            if (!agent.MemoryManager.LongTerm.All.ContainsKey(kvp.Key))
                            {
                                agent.MemoryManager.LongTerm.Save(kvp.Key, kvp.Value, "migrated", FactPriority.Normal);
                                migratedCount++;
                            }
                        }
                    }
                }

                // Мигрируем факты из StickyFacts
                if (agent.ContextManager.StickyFacts is { } sf)
                {
                    foreach (var kvp in sf.Facts)
                    {
                        if (!agent.MemoryManager.LongTerm.All.ContainsKey(kvp.Key))
                        {
                            agent.MemoryManager.LongTerm.Save(kvp.Key, kvp.Value, "migrated", FactPriority.Normal);
                            migratedCount++;
                        }
                    }
                }

                if (migratedCount > 0)
                {
                    agent.Logger.Info($"Миграция: перенесено {migratedCount} факт(ов) из веток/StickyFacts в LongTermMemory");
                }
            }

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
