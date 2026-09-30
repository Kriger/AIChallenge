using AIChallenge.DocumentIndexing.Models;

namespace AIChallenge.DocumentIndexing.Chunking;

/// <summary>
/// Интерфейс для стратегий разбиения текста на чанки.
/// </summary>
public interface IChunkingStrategy
{
    /// <summary>
    /// Название стратегии (для метаданных и сравнения).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Разбивает текст на чанки с метаданными.
    /// </summary>
    /// <param name="text">Исходный текст для разбиения.</param>
    /// <param name="source">Путь к источнику/файлу.</param>
    /// <param name="title">Заголовок документа.</param>
    /// <param name="section">Название раздела (опционально).</param>
    /// <returns>Список чанков с метаданными.</returns>
    IEnumerable<DocumentChunk> Chunk(string text, string source, string title, string section = "");
}
