using System.Text.Json;
using System.Text.Json.Serialization;
using AIChallenge.Core.Infrastructure;

namespace AIChallenge.Core.Infrastructure;

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
/// Персистентность кэша ответов.
/// Сохраняет/загружает из memory/cache.json.
/// Просроченные записи не сохраняются.
/// </summary>
public static class CachePersistence
{
    private const string MemoryDir = "memory";
    private const string FileName = "cache.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string FilePath => Path.GetFullPath(Path.Combine(MemoryDir, FileName));

    /// <summary>
    /// Гарантирует, что директория memory существует.
    /// </summary>
    private static void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Сохраняет кэш в JSON-файл.
    /// Просроченные записи автоматически отфильтровываются.
    /// </summary>
    public static void Save(RequestCache cache)
    {
        EnsureDirectory();

        // Используем рефлексию для доступа к приватному ConcurrentDictionary
        var cacheField = typeof(RequestCache).GetField("_cache",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (cacheField?.GetValue(cache) is not System.Collections.Concurrent.ConcurrentDictionary<string, CachedEntry> cacheDict)
            return;

        var validEntries = cacheDict
            .Where(kvp => kvp.Value.ExpiresAt > DateTime.UtcNow)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new CachedEntryDto
                {
                    Answer = kvp.Value.Answer,
                    AddedAt = kvp.Value.AddedAt,
                    ExpiresAt = kvp.Value.ExpiresAt,
                },
                StringComparer.OrdinalIgnoreCase);

        var json = JsonSerializer.Serialize(validEntries, Options);
        File.WriteAllText(FilePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// Загружает кэш из JSON-файла.
    /// Просроченные записи автоматически отфильтровываются.
    /// </summary>
    public static void Load(RequestCache cache)
    {
        if (!File.Exists(FilePath))
            return;

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var entries = JsonSerializer.Deserialize<Dictionary<string, CachedEntryDto>>(json, Options);

            if (entries is null) return;

            var validEntries = entries
                .Where(kvp => kvp.Value.ExpiresAt > DateTime.UtcNow)
                .Select(kvp => new CachedEntry
                {
                    Answer = kvp.Value.Answer,
                    AddedAt = kvp.Value.AddedAt,
                    ExpiresAt = kvp.Value.ExpiresAt,
                });

            cache.LoadEntries(validEntries);
        }
        catch
        {
            // Тихая ошибка — начинаем с пустого кэша
        }
    }
}
