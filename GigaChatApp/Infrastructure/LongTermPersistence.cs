using System.Text.Json;
using System.Text.Json.Serialization;
using GigaChatApp.Infrastructure;
using GigaChatApp.Models;

namespace GigaChatApp.Infrastructure;

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
/// Персистентность долгосрочной памяти (знания, факты).
/// Сохраняет/загружает из memory/long_term/facts.json.
/// </summary>
public static class LongTermPersistence
{
    private const string MemoryDir = "memory/long_term";
    private const string FileName = "facts.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string FilePath => Path.GetFullPath(Path.Combine(MemoryDir, FileName));

    /// <summary>
    /// Гарантирует, что директория long_term существует.
    /// </summary>
    private static void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Сохраняет долгосрочную память в JSON-файл.
    /// </summary>
    public static void Save(LongTermMemory memory)
    {
        EnsureDirectory();

        var facts = memory.All.ToDictionary(
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
            StringComparer.OrdinalIgnoreCase);

        var json = JsonSerializer.Serialize(facts, Options);
        File.WriteAllText(FilePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// Загружает долгосрочную память из JSON-файла.
    /// </summary>
    public static void Load(LongTermMemory memory)
    {
        if (!File.Exists(FilePath))
            return;

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var facts = JsonSerializer.Deserialize<Dictionary<string, FactDto>>(json, Options);

            if (facts is null) return;

            memory.Clear();

            foreach (var kvp in facts)
            {
                var factDto = kvp.Value;
                var priority = (FactPriority)factDto.Priority;
                memory.Save(factDto.Key, factDto.Value, factDto.Source, priority);

                // Восстанавливаем метрики чтения
                if (memory.All.TryGetValue(kvp.Key, out var fact))
                {
                    fact.LastReadAt = factDto.LastReadAt;
                    fact.ReadCount = factDto.ReadCount;
                }
            }
        }
        catch
        {
            // Тихая ошибка — начинаем с чистой памяти
        }
    }
}
