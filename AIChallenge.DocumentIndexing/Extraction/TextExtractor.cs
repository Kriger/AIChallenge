namespace AIChallenge.DocumentIndexing.Extraction;

/// <summary>
/// Извлекает текст из текстовых файлов и Markdown.
/// </summary>
public class TextExtractor : IDocumentExtractor
{
    public string FormatName => "text";
    public string[] Extensions => new[] { ".txt", ".md", ".cs", ".py", ".js", ".json", ".xml", ".yaml", ".yml" };

    /// <summary>
    /// Извлекает текст из файла.
    /// </summary>
    public async Task<string> ExtractAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        return await File.ReadAllTextAsync(filePath, Encoding.UTF8);
    }
}
