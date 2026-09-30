namespace AIChallenge.DocumentIndexing.Models;

/// <summary>
/// Метаданные чанка документа.
/// Содержит информацию о происхождении чанка для фильтрации и контекста.
/// </summary>
public record ChunkMetadata
{
    /// <summary>Путь к исходному файлу/источнику.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>Заголовок или название документа/раздела.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Название раздела/секции, к которой принадлежит чанк.</summary>
    [JsonPropertyName("section")]
    public string Section { get; set; } = string.Empty;

    /// <summary>Уникальный идентификатор чанка.</summary>
    [JsonPropertyName("chunk_id")]
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>Номер чанка в документе (начиная с 0).</summary>
    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; set; }

    /// <summary>Общее количество чанков в документе.</summary>
    [JsonPropertyName("total_chunks")]
    public int TotalChunks { get; set; }

    /// <summary>Стратегия чанкинга, которая была использована.</summary>
    [JsonPropertyName("chunking_strategy")]
    public string ChunkingStrategy { get; set; } = string.Empty;

    /// <summary>Количество символов в чанке.</summary>
    [JsonPropertyName("char_count")]
    public int CharCount { get; set; }

    /// <summary>Время создания чанка.</summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
