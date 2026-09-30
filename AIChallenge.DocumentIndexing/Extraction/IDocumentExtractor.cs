namespace AIChallenge.DocumentIndexing.Extraction;

/// <summary>
/// Извлекает текст из различных форматов документов.
/// </summary>
public interface IDocumentExtractor
{
    /// <summary>
    /// Название поддерживаемого формата.
    /// </summary>
    string FormatName { get; }

    /// <summary>
    /// Расширения файлов, которые поддерживает экстрактор.
    /// </summary>
    string[] Extensions { get; }

    /// <summary>
    /// Извлекает текст из файла.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <returns>Извлечённый текст.</returns>
    Task<string> ExtractAsync(string filePath);
}
