using System.Text.Json;
using System.Text.Json.Serialization;
using GigaChatApp.Infrastructure;

namespace GigaChatApp.Infrastructure;

/// <summary>
/// Сериализуемая версия ShortTermEntry для JSON.
/// </summary>
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

    [JsonPropertyName("isDecayed")]
    public bool IsDecayed { get; set; }
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

    [JsonPropertyName("decayEnabled")]
    public bool DecayEnabled { get; set; } = false;

    [JsonPropertyName("decayAfterHours")]
    public int DecayAfterHours { get; set; } = 1;
}

/// <summary>
/// Персистентность краткосрочной памяти (диалог).
/// Сохраняет/загружает из short_term.json.
/// </summary>
public static class ShortTermPersistence
{
    private const string FileName = "short_term.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string FilePath => Path.GetFullPath(FileName);

    /// <summary>
    /// Сохраняет краткосрочную память в JSON-файл.
    /// </summary>
    public static void Save(ShortTermMemory memory)
    {
        var dto = new ShortTermMemoryDto
        {
            Entries = memory.GetAll().Select(e => new ShortTermEntryDto
            {
                Role = e.Role,
                Content = e.Content,
                Metadata = e.Metadata,
                Timestamp = e.Timestamp,
                IsDecayed = e.IsDecayed,
            }).ToList(),
            MaxSize = memory.MaxSize,
            DecayEnabled = memory.DecayEnabled,
            DecayAfterHours = memory.DecayAfterHours,
        };

        var json = JsonSerializer.Serialize(dto, Options);
        File.WriteAllText(FilePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// Загружает краткосрочную память из JSON-файла.
    /// </summary>
    public static void Load(ShortTermMemory memory)
    {
        if (!File.Exists(FilePath))
            return;

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var dto = JsonSerializer.Deserialize<ShortTermMemoryDto>(json, Options);

            if (dto is null) return;

            memory.Clear();

            foreach (var entry in dto.Entries)
            {
                memory.Add(entry.Role, entry.Content, entry.Metadata);
                // Восстанавливаем IsDecayed через рефлексию
                var last = memory.Last;
                if (last is not null)
                {
                    var prop = typeof(MemoryEntry).GetProperty("IsDecayed");
                    prop?.SetValue(last, entry.IsDecayed);
                }
            }

            memory.MaxSize = dto.MaxSize;
            memory.DecayEnabled = dto.DecayEnabled;
            memory.DecayAfterHours = dto.DecayAfterHours;
        }
        catch
        {
            // Тихая ошибка — начинаем с чистой памяти
        }
    }
}
