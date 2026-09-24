using System.Text.Json;
using System.Text.Json.Serialization;
using AIChallenge.Core.Infrastructure;

namespace AIChallenge.Core.Infrastructure;

/// <summary>
/// Сериализуемая версия WorkingEntry для JSON.
/// </summary>
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

/// <summary>
/// Персистентность рабочей памяти (текущая задача).
/// Сохраняет/загружает из memory/working/current.json.
/// </summary>
public static class WorkingMemoryPersistence
{
    private static string MemoryDir => Path.Combine(AppContext.BaseDirectory, "memory", "working");
    private const string FileName = "current.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string FilePath => Path.GetFullPath(Path.Combine(MemoryDir, FileName));

    /// <summary>
    /// Гарантирует, что директория working существует.
    /// </summary>
    private static void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Сохраняет рабочую память в JSON-файл.
    /// </summary>
    public static void Save(WorkingMemory memory)
    {
        EnsureDirectory();

        var dto = new WorkingMemoryDto
        {
            Entries = memory.All.ToDictionary(
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
            CurrentTaskId = memory.CurrentTaskId,
            CurrentTaskStatus = memory.CurrentTaskStatus,
            TaskStartedAt = memory.TaskStartedAt,
            ArchiveEnabled = memory.ArchiveEnabled,
            MaxArchiveSize = memory.MaxArchiveSize,
            ArchivedTasks = memory.AllArchived.ToDictionary(
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
        };

        var json = JsonSerializer.Serialize(dto, Options);
        File.WriteAllText(FilePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// Загружает рабочую память из JSON-файла.
    /// </summary>
    public static void Load(WorkingMemory memory)
    {
        if (!File.Exists(FilePath))
            return;

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var dto = JsonSerializer.Deserialize<WorkingMemoryDto>(json, Options);

            if (dto is null) return;

            memory.Clear();

            // Восстанавливаем основные записи
            foreach (var kvp in dto.Entries)
            {
                var entry = kvp.Value;
                memory.Save(entry.Key, entry.Value, entry.Type);
                if (memory.All.TryGetValue(kvp.Key, out var workingEntry))
                {
                    workingEntry.TaskId = entry.TaskId;
                    workingEntry.CreatedAt = entry.CreatedAt;
                    workingEntry.UpdatedAt = entry.UpdatedAt;
                }
            }

            memory.CurrentTaskId = dto.CurrentTaskId;
            memory.CurrentTaskStatus = dto.CurrentTaskStatus;
            memory.TaskStartedAt = dto.TaskStartedAt;
            memory.ArchiveEnabled = dto.ArchiveEnabled;
            memory.MaxArchiveSize = dto.MaxArchiveSize;

            // Восстанавливаем архив через рефлексию
            var archivedField = typeof(WorkingMemory).GetField("_archivedTasks",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (archivedField?.GetValue(memory) is Dictionary<string, List<WorkingEntry>> archivedDict)
            {
                foreach (var archKvp in dto.ArchivedTasks)
                {
                    if (!archivedDict.ContainsKey(archKvp.Key))
                    {
                        archivedDict[archKvp.Key] = new List<WorkingEntry>();
                    }
                    foreach (var archEntry in archKvp.Value)
                    {
                        archivedDict[archKvp.Key].Add(new WorkingEntry
                        {
                            Key = archEntry.Key,
                            Value = archEntry.Value,
                            Type = archEntry.Type,
                            TaskId = archEntry.TaskId,
                            CreatedAt = archEntry.CreatedAt,
                            UpdatedAt = archEntry.UpdatedAt,
                        });
                    }
                }
            }
        }
        catch
        {
            // Тихая ошибка — начинаем с чистой памяти
        }
    }
}
