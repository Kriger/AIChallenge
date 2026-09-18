using System.Text.Json;
using System.Text.Json.Serialization;
using GigaChatApp.Infrastructure;

namespace GigaChatApp.Infrastructure;

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
/// Сериализуемая версия метрик агента для JSON.
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

    [JsonPropertyName("contextComparison")]
    public ContextComparisonMetricsDto ContextComparison { get; set; } = new();
}

/// <summary>
/// Персистентность метрик агента.
/// Сохраняет/загружает из memory/metrics.json.
/// </summary>
public static class MetricsPersistence
{
    private const string MemoryDir = "memory";
    private const string FileName = "metrics.json";

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
    /// Сохраняет метрики агента в JSON-файл.
    /// </summary>
    public static void Save(AgentMetrics metrics)
    {
        EnsureDirectory();

        var dto = new MetricsDto
        {
            TotalRequests = metrics.TotalRequests,
            SuccessfulRequests = metrics.SuccessfulRequests,
            FailedRequests = metrics.FailedRequests,
            CachedRequests = metrics.CachedRequests,
            RetryAttempts = metrics.RetryAttempts,
            RetryCount = metrics.RetryCount,
            RetryDelayMs = metrics.RetryDelayMs,
            TotalDurationTicks = metrics.TotalDuration.Ticks,
            TotalPromptTokens = metrics.TotalPromptTokens,
            TotalCompletionTokens = metrics.TotalCompletionTokens,
            TotalContextTokens = metrics.TotalContextTokens,
            LastContextTokens = metrics.LastContextTokens,
            ContextCompressionEnabled = metrics.ContextCompressionEnabled,
            ContextComparison = new ContextComparisonMetricsDto
            {
                ComparisonCount = metrics.ContextComparison.ComparisonCount,
                TotalOriginalTokens = metrics.ContextComparison.TotalOriginalTokens,
                TotalCompressedTokens = metrics.ContextComparison.TotalCompressedTokens,
                TotalReplacedMessages = metrics.ContextComparison.TotalReplacedMessages,
                TotalSentMessages = metrics.ContextComparison.TotalSentMessages,
                MaxTokenSavings = metrics.ContextComparison.MaxTokenSavings,
            },
        };

        var json = JsonSerializer.Serialize(dto, Options);
        File.WriteAllText(FilePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// Загружает метрики агента из JSON-файла.
    /// </summary>
    public static void Load(AgentMetrics metrics)
    {
        if (!File.Exists(FilePath))
            return;

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var dto = JsonSerializer.Deserialize<MetricsDto>(json, Options);

            if (dto is null) return;

            metrics.TotalRequests = dto.TotalRequests;
            metrics.SuccessfulRequests = dto.SuccessfulRequests;
            metrics.FailedRequests = dto.FailedRequests;
            metrics.CachedRequests = dto.CachedRequests;
            metrics.RetryAttempts = dto.RetryAttempts;
            metrics.RetryCount = dto.RetryCount;
            metrics.RetryDelayMs = dto.RetryDelayMs;
            metrics.TotalDuration = TimeSpan.FromTicks(dto.TotalDurationTicks);
            metrics.TotalPromptTokens = dto.TotalPromptTokens;
            metrics.TotalCompletionTokens = dto.TotalCompletionTokens;
            metrics.TotalContextTokens = dto.TotalContextTokens;
            metrics.LastContextTokens = dto.LastContextTokens;
            metrics.ContextCompressionEnabled = dto.ContextCompressionEnabled;

            if (dto.ContextComparison is not null)
            {
                metrics.ContextComparison = new ContextComparisonMetrics
                {
                    ComparisonCount = dto.ContextComparison.ComparisonCount,
                    TotalOriginalTokens = dto.ContextComparison.TotalOriginalTokens,
                    TotalCompressedTokens = dto.ContextComparison.TotalCompressedTokens,
                    TotalReplacedMessages = dto.ContextComparison.TotalReplacedMessages,
                    TotalSentMessages = dto.ContextComparison.TotalSentMessages,
                    MaxTokenSavings = dto.ContextComparison.MaxTokenSavings,
                };
            }
        }
        catch
        {
            // Тихая ошибка — начинаем с нуля
        }
    }
}
