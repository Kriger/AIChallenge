using AIChallenge.DocumentIndexing.Chunking;
using AIChallenge.DocumentIndexing.Models;

namespace AIChallenge.DocumentIndexing.Extraction;

/// <summary>
/// Загружает документы из файлов, автоматически определяя формат.
/// Поддерживает: PDF, TXT, MD, CS, PY и другие текстовые форматы.
/// </summary>
public class DocumentLoader
{
    private readonly List<IDocumentExtractor> _extractors;

    /// <summary>
    /// Создаёт загрузчик документов.
    /// </summary>
    public DocumentLoader()
    {
        _extractors = new List<IDocumentExtractor>
        {
            new PdfExtractor(),
            new TextExtractor(),
        };
    }

    /// <summary>
    /// Загружает один документ.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <returns>Текст документа и метаданные.</returns>
    public async Task<(string Text, string Source, string Title)> LoadDocumentAsync(string filePath)
    {
        var extractor = GetExtractorForFile(filePath)
            ?? throw new InvalidOperationException($"Неизвестный формат файла: {filePath}");
        var text = await extractor.ExtractAsync(filePath);
        var title = Path.GetFileNameWithoutExtension(filePath);

        return (text, filePath, title);
    }

    /// <summary>
    /// Загружает все документы из папки.
    /// Рекурсивно ищет файлы поддерживаемых форматов.
    /// </summary>
    /// <param name="directoryPath">Путь к папке.</param>
    /// <param name="recursive">Искать рекурсивно.</param>
    /// <returns>Список загруженных документов.</returns>
    public async Task<List<(string Text, string Source, string Title)>> LoadDocumentsAsync(
        string directoryPath, bool recursive = true)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directoryPath, "*.*", searchOption)
            .Where(f => GetExtractorForFile(f) != null)
            .ToList();

        var documents = new List<(string Text, string Source, string Title)>();

        foreach (var file in files)
        {
            try
            {
                var (text, source, title) = await LoadDocumentAsync(file);
                documents.Add((text, source, title));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Ошибка загрузки {file}: {ex.Message}");
            }
        }

        return documents;
    }

    /// <summary>
    /// Загружает документы из списка файлов.
    /// </summary>
    /// <param name="filePaths">Список путей к файлам.</param>
    /// <returns>Список загруженных документов.</returns>
    public async Task<List<(string Text, string Source, string Title)>> LoadDocumentsAsync(
        IEnumerable<string> filePaths)
    {
        var documents = new List<(string Text, string Source, string Title)>();

        foreach (var filePath in filePaths)
        {
            try
            {
                var (text, source, title) = await LoadDocumentAsync(filePath);
                documents.Add((text, source, title));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Ошибка загрузки {filePath}: {ex.Message}");
            }
        }

        return documents;
    }

    /// <summary>
    /// Определяет экстрактор для файла по расширению.
    /// </summary>
    private IDocumentExtractor? GetExtractorForFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        foreach (var extractor in _extractors)
        {
            if (extractor.Extensions.Contains(extension))
                return extractor;
        }

        return null;
    }

    /// <summary>
    /// Получает список поддерживаемых форматов.
    /// </summary>
    public string GetSupportedFormats()
    {
        var formats = _extractors
            .Select(e => $"{e.FormatName}: {string.Join(", ", e.Extensions)}")
            .ToList();

        return string.Join(" | ", formats);
    }
}
