using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIChallenge.DocumentIndexing.Models;

/// <summary>
/// Чанк документа — единица текста для индексации.
/// Содержит текст чанка, его метаданные и эмбеддинг.
/// </summary>
public class DocumentChunk
{
    /// <summary>Уникальный идентификатор чанка.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Текстовое содержимое чанка.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Метаданные чанка.</summary>
    [JsonPropertyName("metadata")]
    public ChunkMetadata Metadata { get; set; } = new();

    /// <summary>Векторное представление чанка (эмбеддинг).</summary>
    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }

    /// <summary>
    /// Создаёт новый чанк с заданным текстом и метаданными.
    /// </summary>
    public static DocumentChunk Create(string text, ChunkMetadata metadata)
    {
        var chunkId = $"{metadata.Source}_{metadata.ChunkIndex:D4}";
        return new DocumentChunk
        {
            Id = chunkId,
            Text = text,
            Metadata = metadata with { ChunkId = chunkId, CharCount = text.Length },
        };
    }

    /// <summary>
    /// Сериализует чанк в JSON для сохранения.
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
    /// Десериализует чанк из JSON.
    /// </summary>
    public static DocumentChunk? FromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        return JsonSerializer.Deserialize<DocumentChunk>(json, options);
    }
}
