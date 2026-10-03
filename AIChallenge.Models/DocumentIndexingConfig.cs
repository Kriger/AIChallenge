namespace AIChallenge.Models;

/// <summary>
/// Конфигурация индексации документов.
/// Загружается из appsettings.json.
/// </summary>
public class DocumentIndexingConfig
{
    /// <summary>
    /// URL Ollama сервера.
    /// </summary>
    public string OllamaUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Модель Ollama для эмбеддингов.
    /// </summary>
    public string OllamaEmbeddingModel { get; set; } = "mxbai-embed-large";

    /// <summary>
    /// Поддиректория для хранения индексов (относительно AppContext.BaseDirectory).
    /// </summary>
    public string IndexSubDir { get; set; } = "memory/document_index";

    /// <summary>
    /// Количество результатов поиска.
    /// </summary>
    public int SearchTopK { get; set; } = 5;

    /// <summary>
    /// Минимальная схожесть для отображения результата поиска (0.0 — 1.0).
    /// </summary>
    public float MinSimilarity { get; set; } = 0.50f;

    /// <summary>
    /// Максимальный размер текстового файла (байт). По умолчанию 5 МБ.
    /// </summary>
    public long MaxTextFileSize { get; set; } = 5L * 1024 * 1024;

    /// <summary>
    /// Максимальный размер PDF файла (байт). По умолчанию 20 МБ.
    /// </summary>
    public long MaxPdfFileSize { get; set; } = 20L * 1024 * 1024;

    /// <summary>
    /// Максимальное количество символов текста для эмбеддинга.
    /// </summary>
    public int MaxTextLength { get; set; } = 20000;

    /// <summary>
    /// Максимальное количество страниц PDF для извлечения текста.
    /// </summary>
    public int MaxPdfPages { get; set; } = 50;

    /// <summary>
    /// Максимальное количество символов на страницу PDF.
    /// </summary>
    public int MaxPdfPageSize { get; set; } = 5000;

    /// <summary>
    /// Поддерживаемые расширения файлов.
    /// </summary>
    public string[] SupportedExtensions { get; set; } =
    [".pdf", ".txt", ".md", ".cs", ".py", ".js", ".json", ".xml", ".yaml", ".yml"];

    /// <summary>
    /// Размер чанка для FixedSizeChunking (символы).
    /// </summary>
    public int ChunkSize { get; set; } = 1000;

    /// <summary>
    /// Перекрытие чанков для FixedSizeChunking (символы).
    /// </summary>
    public int ChunkOverlap { get; set; } = 100;

    /// <summary>
    /// Максимальное количество файлов для сравнения стратегий.
    /// </summary>
    public int CompareMaxFiles { get; set; } = 3;

    /// <summary>
    /// Максимальная длина текста для чтения (символы).
    /// </summary>
    public int ReadTextMaxLength { get; set; } = 20000;

    /// <summary>
    /// Размер буфера чтения текста (байты).
    /// </summary>
    public int ReadTextBufferSize { get; set; } = 4096;
}
