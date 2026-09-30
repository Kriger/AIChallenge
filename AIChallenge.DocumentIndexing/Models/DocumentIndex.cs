using AIChallenge.DocumentIndexing.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIChallenge.DocumentIndexing.Models;

/// <summary>
/// Индекс документов — коллекция чанков с метаданными и конфигурацией.
/// Используется для сохранения и загрузки индекса.
/// </summary>
public class DocumentIndex
{
    /// <summary>Версия формата индекса.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>Дата создания индекса.</summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Количество чанков в индексе.</summary>
    [JsonPropertyName("chunk_count")]
    public int ChunkCount { get; set; }

    /// <summary>Размерность эмбеддингов.</summary>
    [JsonPropertyName("embedding_dimension")]
    public int EmbeddingDimension { get; set; }

    /// <summary>Стратегии чанкинга, использованные для создания индекса.</summary>
    [JsonPropertyName("chunking_strategies")]
    public List<string> ChunkingStrategies { get; set; } = new();

    /// <summary>Список всех чанков в индексе.</summary>
    [JsonPropertyName("chunks")]
    public List<DocumentChunk> Chunks { get; set; } = new();

    /// <summary>
    /// Добавляет чанк в индекс.
    /// </summary>
    public void AddChunk(DocumentChunk chunk)
    {
        Chunks.Add(chunk);
        ChunkCount = Chunks.Count;
        
        if (chunk.Embedding is not null)
        {
            EmbeddingDimension = chunk.Embedding.Length;
        }
    }

    /// <summary>
    /// Добавляет несколько чанков в индекс.
    /// </summary>
    public void AddChunks(IEnumerable<DocumentChunk> chunks)
    {
        Chunks.AddRange(chunks);
        ChunkCount = Chunks.Count;

        var maxDim = Chunks
            .Where(c => c.Embedding is not null)
            .Select(c => c.Embedding!.Length)
            .DefaultIfEmpty()
            .Max();
        
        EmbeddingDimension = maxDim;
    }

    /// <summary>
    /// Фильтрует чанки по метаданным.
    /// </summary>
    public IEnumerable<DocumentChunk> FilterBySource(string source)
    {
        return Chunks.Where(c => c.Metadata.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Фильтрует чанки по стратегии чанкинга.
    /// </summary>
    public IEnumerable<DocumentChunk> FilterByStrategy(string strategy)
    {
        return Chunks.Where(c => c.Metadata.ChunkingStrategy.Equals(strategy, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Сериализует индекс в JSON.
    /// </summary>
    public string ToJson()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        return JsonSerializer.Serialize(this, options);
    }

    /// <summary>
    /// Десериализует индекс из JSON.
    /// </summary>
    public static DocumentIndex? FromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        return JsonSerializer.Deserialize<DocumentIndex>(json, options);
    }
}
