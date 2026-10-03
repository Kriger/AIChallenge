using AIChallenge.DocumentIndexing.Models;
using System.Collections.Generic;

namespace AIChallenge.DocumentIndexing.Chunking;

/// <summary>
/// Стратегия чанкинга по фиксированному размеру.
/// Разбивает текст на чанки заданной длины с возможностью перекрытия.
/// </summary>
public class FixedSizeChunking : IChunkingStrategy
{
    /// <summary>
    /// Название стратегии.
    /// </summary>
    public string Name => "fixed_size";

    private readonly int _chunkSize;
    private readonly int _overlap;

    /// <summary>
    /// Создаёт стратегию фиксированного размера.
    /// </summary>
    /// <param name="chunkSize">Размер чанка в символах (по умолчанию 500).</param>
    /// <param name="overlap">Перекрытие между чанками в символах (по умолчанию 50).</param>
    public FixedSizeChunking(int chunkSize = 500, int overlap = 50)
    {
        if (chunkSize <= 0)
            throw new ArgumentException("Chunk size must be positive", nameof(chunkSize));
        if (overlap < 0)
            throw new ArgumentException("Overlap cannot be negative", nameof(overlap));
        if (overlap >= chunkSize)
            throw new ArgumentException("Overlap must be less than chunk size", nameof(overlap));

        _chunkSize = chunkSize;
        _overlap = overlap;
    }

    /// <summary>
    /// Разбивает текст на чанки фиксированного размера.
    /// Каждый чанк обогащается контекстом: заголовок документа + название раздела.
    /// </summary>
    public IEnumerable<DocumentChunk> Chunk(string text, string source, string title, string section = "")
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var totalChunks = (int)Math.Ceiling((double)text.Length / (_chunkSize - _overlap));
        var startIndex = 0;
        var chunkIndex = 0;

        // Формируем префикс контекста один раз
        var contextPrefix = BuildContextPrefix(title, section);

        while (startIndex < text.Length)
        {
            var endIndex = Math.Min(startIndex + _chunkSize, text.Length);
            var chunkText = text.Substring(startIndex, endIndex - startIndex);

            // Попытка разбить по слову, если не конец текста
            if (endIndex < text.Length)
            {
                var lastSpace = chunkText.LastIndexOf(' ');
                if (lastSpace > _chunkSize / 2)
                {
                    chunkText = chunkText.Substring(0, lastSpace).TrimEnd();
                    endIndex = startIndex + chunkText.Length;
                }
            }

            // Обогащаем чанк контекстом
            var enrichedText = contextPrefix + "\n\n" + chunkText.Trim();

            yield return DocumentChunk.Create(enrichedText, new ChunkMetadata
            {
                Source = source,
                Title = title,
                Section = section,
                ChunkIndex = chunkIndex,
                TotalChunks = totalChunks,
                ChunkingStrategy = Name,
                CreatedAt = DateTime.UtcNow,
            });
            chunkIndex++;

            var newStartIndex = endIndex - _overlap;
            // Защита от бесконечного цикла: если чанк короткий, двигаемся без перекрытия
            if (newStartIndex <= startIndex)
                newStartIndex = endIndex;
            startIndex = newStartIndex;
            if (startIndex >= text.Length)
                break;
        }
    }

    /// <summary>
    /// Формирует префикс контекста для чанка.
    /// </summary>
    private static string BuildContextPrefix(string documentTitle, string sectionTitle)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(documentTitle))
            parts.Add($"# {documentTitle}");

        if (!string.IsNullOrWhiteSpace(sectionTitle))
            parts.Add($"## {sectionTitle}");

        return string.Join("\n", parts);
    }
}
