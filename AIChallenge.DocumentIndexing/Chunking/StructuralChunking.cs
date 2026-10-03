using AIChallenge.DocumentIndexing.Models;

namespace AIChallenge.DocumentIndexing.Chunking;

/// <summary>
/// Стрататегия чанкинга по структуре документа.
/// Разбивает текст по заголовкам (Markdown, заголовки с # или ALL CAPS, или пустые строки).
/// </summary>
public class StructuralChunking : IChunkingStrategy
{
    /// <summary>
    /// Название стратегии.
    /// </summary>
    public string Name => "structural";

    private readonly int _minChunkSize;
    private readonly int _maxChunkSize;

    /// <summary>
    /// Создаёт стратегию структурного чанкинга.
    /// </summary>
    /// <param name="minChunkSize">Минимальный размер чанка в символах (по умолчанию 100).</param>
    /// <param name="maxChunkSize">Максимальный размер чанка в символах (по умолчанию 2000).</param>
    public StructuralChunking(int minChunkSize = 100, int maxChunkSize = 2000)
    {
        if (minChunkSize <= 0)
            throw new ArgumentException("Min chunk size must be positive", nameof(minChunkSize));
        if (maxChunkSize <= minChunkSize)
            throw new ArgumentException("Max chunk size must be greater than min chunk size", nameof(maxChunkSize));

        _minChunkSize = minChunkSize;
        _maxChunkSize = maxChunkSize;
    }

    /// <summary>
    /// Разбивает текст на чанки по структуре (заголовкам/разделам).
    /// Каждый чанк обогащается контекстом: заголовок документа, название раздела.
    /// </summary>
    public IEnumerable<DocumentChunk> Chunk(string text, string source, string title, string section = "")
    {
        if (string.IsNullOrWhiteSpace(text))
            return Enumerable.Empty<DocumentChunk>();

        var sections = SplitByStructure(text);
        var chunks = new List<DocumentChunk>();
        var totalSections = sections.Count;
        var chunkIndex = 0;

        foreach (var (sectionTitle, sectionText) in sections)
        {
            // Если раздел слишком большой, разбиваем его на части
            if (sectionText.Length > _maxChunkSize)
            {
                var subChunks = SplitLargeSection(sectionText, source, title, sectionTitle);
                foreach (var subChunk in subChunks)
                {
                    chunks.Add(subChunk);
                    chunkIndex++;
                }
            }
            else if (sectionText.Length >= _minChunkSize)
            {
                // Обогащаем чанк контекстом
                var enrichedText = EnrichWithContext(title, sectionTitle, sectionText);
                var metadata = new ChunkMetadata
                {
                    Source = source,
                    Title = title,
                    Section = sectionTitle,
                    ChunkIndex = chunkIndex,
                    TotalChunks = totalSections,
                    ChunkingStrategy = Name,
                    CreatedAt = DateTime.UtcNow,
                };

                chunks.Add(DocumentChunk.Create(enrichedText, metadata));
                chunkIndex++;
            }
        }

        return chunks;
    }

    /// <summary>
    /// Обогащает текст чанка контекстом: заголовок документа + название раздела.
    /// Это критично для поиска — чанк должен содержать ключевые слова из заголовка.
    /// </summary>
    private static string EnrichWithContext(string documentTitle, string sectionTitle, string text)
    {
        var parts = new List<string>();

        // Заголовок документа как H1
        if (!string.IsNullOrWhiteSpace(documentTitle))
        {
            parts.Add($"# {documentTitle}");
        }

        // Заголовок раздела как H2
        if (!string.IsNullOrWhiteSpace(sectionTitle))
        {
            parts.Add($"## {sectionTitle}");
        }

        // Тело раздела
        parts.Add(text.Trim());

        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Разбивает текст на разделы по заголовкам.
    /// Поддерживает Markdown-заголовки (#), заголовки ALL CAPS, и разделители (---).
    /// </summary>
    private static List<(string Title, string Text)> SplitByStructure(string text)
    {
        var sections = new List<(string Title, string Text)>();
        var currentTitle = "Introduction";
        var currentLines = new List<string>();

        // Читаем построчно через StreamReader — не грузим весь файл в память
        using var sr = new StringReader(text);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            var trimmed = line.Trim();

            // Markdown заголовок (# Title)
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                if (currentLines.Count > 0)
                {
                    sections.Add((currentTitle, string.Join("\n", currentLines)));
                    currentLines.Clear();
                }
                currentTitle = trimmed.Replace("#", "").Trim();
            }
            // Заголовок ALL CAPS (минимум 3 символа)
            else if (trimmed.Length >= 3 && trimmed == trimmed.ToUpperInvariant() && !trimmed.Any(char.IsWhiteSpace))
            {
                if (currentLines.Count > 0)
                {
                    sections.Add((currentTitle, string.Join("\n", currentLines)));
                    currentLines.Clear();
                }
                currentTitle = trimmed;
            }
            // Разделитель (---, ***, ===)
            else if (trimmed.Length > 0 && trimmed.All(c => c == '-' || c == '*' || c == '='))
            {
                if (currentLines.Count > 0)
                {
                    sections.Add((currentTitle, string.Join("\n", currentLines)));
                    currentLines.Clear();
                }
                currentTitle = $"Section {sections.Count + 1}";
            }
            else
            {
                currentLines.Add(trimmed);
            }
        }

        // Добавляем последний раздел
        if (currentLines.Count > 0)
        {
            sections.Add((currentTitle, string.Join("\n", currentLines)));
        }

        // Если не было заголовков, возвращаем весь текст как один раздел
        if (sections.Count == 0)
        {
            sections.Add(("Full Document", text.Trim()));
        }

        return sections;
    }

    /// <summary>
    /// Разбивает большой раздел на части фиксированного размера.
    /// Каждый под-чанк обогащается контекстом.
    /// </summary>
    private DocumentChunk[] SplitLargeSection(string text, string source, string title, string sectionTitle)
    {
        var chunks = new List<DocumentChunk>();
        var chunkSize = _maxChunkSize;
        var overlap = (int)(_maxChunkSize * 0.1); // 10% overlap
        var startIndex = 0;
        var localChunkIndex = 0;

        while (startIndex < text.Length)
        {
            var endIndex = Math.Min(startIndex + chunkSize, text.Length);
            var chunkText = text.Substring(startIndex, endIndex - startIndex);

            if (endIndex < text.Length)
            {
                var lastSpace = chunkText.LastIndexOf(' ');
                if (lastSpace > chunkSize / 2)
                {
                    chunkText = chunkText.Substring(0, lastSpace).TrimEnd();
                    endIndex = startIndex + chunkText.Length;
                }
            }

            // Обогащаем контекстом
            var enrichedText = EnrichWithContext(title, sectionTitle, chunkText);

            var metadata = new ChunkMetadata
            {
                Source = source,
                Title = title,
                Section = sectionTitle,
                ChunkIndex = localChunkIndex,
                TotalChunks = (int)Math.Ceiling((double)text.Length / (chunkSize - overlap)),
                ChunkingStrategy = Name,
                CreatedAt = DateTime.UtcNow,
            };

            chunks.Add(DocumentChunk.Create(enrichedText, metadata));
            localChunkIndex++;
            startIndex = endIndex - overlap;

            if (startIndex >= text.Length)
                break;
        }

        return chunks.ToArray();
    }
}
