using AIChallenge.DocumentIndexing.Chunking;
using AIChallenge.DocumentIndexing.Embeddings;
using AIChallenge.DocumentIndexing.Indexing;
using AIChallenge.DocumentIndexing.Models;
using System.Text.Json;

namespace AIChallenge.DocumentIndexing;

/// <summary>
/// Результаты сравнения стратегий чанкинга.
/// </summary>
public class ChunkingComparisonResult
{
    public string StrategyName { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public float AverageChunkSize { get; set; }
    public int MinChunkSize { get; set; }
    public int MaxChunkSize { get; set; }
    public int TotalCharacters { get; set; }
    public long ChunkingTimeMs { get; set; }
    public long EmbeddingTimeMs { get; set; }
    public long TotalTimeMs => ChunkingTimeMs + EmbeddingTimeMs;

    public string ToReport()
    {
        return $"""
            Стратегия: {StrategyName}
            ├── Чанков: {ChunkCount}
            ├── Средний размер: {AverageChunkSize:F0} символов
            ├── Размер чанков: {MinChunkSize} - {MaxChunkSize}
            ├── Общее количество символов: {TotalCharacters}
            ├── Время чанкинга: {ChunkingTimeMs} мс
            └── Время эмбеддингов: {EmbeddingTimeMs} мс
                Общее время: {TotalTimeMs} мс
            """;
    }
}

/// <summary>
/// Пайплайн индексации документов.
/// 
/// Ключевое отличие: потоковая обработка.
/// - Не загружает все тексты в память
/// - Обрабатывает один файл за раз: загрузка → чанкинг → эмбеддинги → сохранение
/// - Эмбеддинги записываются на диск сразу после генерации
/// - Память: O(1) относительно количества файлов
/// </summary>
public class DocumentIndexer
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly FaissIndexStorage _indexStorage;
    private readonly Action<string>? _logAction;

    public DocumentIndexer(IEmbeddingProvider embeddingProvider, Action<string>? logAction = null)
    {
        _embeddingProvider = embeddingProvider;
        _indexStorage = new FaissIndexStorage();
        _logAction = logAction;
    }

    /// <summary>
    /// Индексирует ВСЕ документы из папки потоково.
    /// </summary>
    public async Task IndexDirectoryAsync(
        string directoryPath,
        IChunkingStrategy chunkingStrategy,
        string outputDir,
        bool recursive = true,
        Action<int, int>? progressCallback = null)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException(directoryPath);

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directoryPath, "*.*", searchOption)
            .Where(f => IsSupportedFile(f))
            .ToArray();

        Log($"📚 Найдено {files.Length} файлов для индексации");
        Log($"🔧 Стратегия: {chunkingStrategy.Name}");
        Log($"📁 Папка: {directoryPath}");
        Console.WriteLine();

        var totalChunks = 0;
        var totalBytes = 0L;

        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            progressCallback?.Invoke(i + 1, files.Length);

            Log($"[{i + 1}/{files.Length}] {Path.GetFileName(file)}...");

            try
            {
                var title = Path.GetFileNameWithoutExtension(file);
                var text = await ExtractTextAsync(file);
                if (string.IsNullOrWhiteSpace(text))
                {
                    Log($"  ⚠️  Пропуск: пустой файл");
                    continue;
                }

                var safeName = SanitizeName(title);
                var indexFilePath = Path.Combine(outputDir, $"index_{_indexStorage.Name}_{chunkingStrategy.Name}_{safeName}");

                // Проход 2: stream JSON + эмбеддинги
                var embStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var embProgress = new Progress<int>(n =>
                    Log($"  Эмбеддинги: {n}"));

                var chunkCount = await StreamIndexAsync(text, file, title, chunkingStrategy, indexFilePath, embProgress);
                embStopwatch.Stop();

                var embSizeBytes = new FileInfo($"{indexFilePath}.bin").Length;
                totalBytes += embSizeBytes;
                totalChunks += chunkCount;

                Log($"  ✅ {embStopwatch.ElapsedMilliseconds} мс, {embSizeBytes / 1024} KB");
            }
            catch (Exception ex)
            {
                Log($"  ❌ Ошибка: {ex.Message}");
            }
        }

        Console.WriteLine();
        Log("=== Итог ===");
        Log($"  Обработано файлов: {files.Length}");
        Log($"  Всего чанков: {totalChunks}");
        var totalMB = totalBytes / 1024.0 / 1024.0;
        Log($"  Размер эмбеддингов: {totalBytes / 1024} KB ({totalMB:F2} MB)");
        Log($"  Сохранено в: {outputDir}");
    }

    /// <summary>
    /// Stream-индексирует ОДИН файл.
    /// </summary>
    public async Task<bool> IndexFileAsync(
        string filePath,
        IChunkingStrategy chunkingStrategy,
        string outputDir)
    {
        if (!File.Exists(filePath))
        {
            Log($"❌ Файл не найден: {filePath}");
            return false;
        }

        var title = Path.GetFileNameWithoutExtension(filePath);
        var text = await ExtractTextAsync(filePath);
        if (string.IsNullOrWhiteSpace(text))
        {
            Log("⚠️  Пустой файл");
            return false;
        }

        var safeName = SanitizeName(title);
        var indexFilePath = Path.Combine(outputDir, $"index_{_indexStorage.Name}_{chunkingStrategy.Name}_{safeName}");

        var embStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var chunkCount = await StreamIndexAsync(text, filePath, title, chunkingStrategy, indexFilePath, null!);
        embStopwatch.Stop();

        var embSize = new FileInfo($"{indexFilePath}.bin").Length;
        Log($"  ✅ {embStopwatch.ElapsedMilliseconds} мс, {embSize / 1024} KB");
        Log($"  📋 {indexFilePath}.json");
        Log($"  💾 {indexFilePath}.bin");

        return chunkCount > 0;
    }

    /// <summary>
    /// Stream-пишет JSON-индекс и эмбеддинги в ОДНОМ проходе по чанкам.
    /// </summary>
    private async Task<int> StreamIndexAsync(
        string text,
        string source,
        string title,
        IChunkingStrategy chunkingStrategy,
        string indexFilePath,
        IProgress<int> embProgress)
    {
        var chunks = new List<DocumentChunk>();
        var embeddings = new List<float[]>();
        var chunkIndex = 0;
        var actualDimension = 0;

        foreach (var chunk in chunkingStrategy.Chunk(text, source, title))
        {
            try
            {
                var vector = await _embeddingProvider.GenerateEmbeddingAsync(chunk.Text);
                if (actualDimension == 0) actualDimension = vector.Length;
                
                chunk.Embedding = vector;
                chunks.Add(chunk);
                embeddings.Add(vector);
                embProgress?.Report(chunkIndex + 1);
            }
            catch (Exception ex)
            {
                var msg = $"  ⚠️  Ошибка эмбеддинга чанка {chunkIndex + 1}: {ex.Message}";
                Log(msg);
            }

            chunkIndex++;
        }

        if (chunks.Count == 0) return 0;

        var index = new DocumentIndex
        {
            Version = "1.0.0",
            CreatedAt = DateTime.UtcNow,
            ChunkCount = chunks.Count,
            EmbeddingDimension = actualDimension,
            ChunkingStrategies = new() { chunkingStrategy.Name },
            Chunks = chunks
        };

        var embeddingsArray = embeddings.ToArray();
        await _indexStorage.SaveAsync(index, embeddingsArray, $"{indexFilePath}.bin");
        
        // Проверяем, что файл реально записан
        var binFileInfo = new FileInfo($"{indexFilePath}.bin");
        Log($"  💾 Бинарный файл: {binFileInfo.Length / 1024} KB ({embeddingsArray.Length} эмбеддингов × {actualDimension} dim)");

        return chunks.Count;
    }



    /// <summary>
    /// Поиск: загружает эмбеддинги по одному, не читая весь файл.
    /// </summary>
    public async Task<List<(DocumentChunk Chunk, float Distance)>> SearchAsync(
        string query,
        string indexJsonPath,
        string embeddingsPath,
        int topK = 5)
    {
        Log($"🔍 Поиск: \"{query}\"");

        var jsonBytes = await File.ReadAllBytesAsync(indexJsonPath);
        jsonBytes = StripUtf8Bom(jsonBytes);
        using var doc = JsonDocument.Parse(jsonBytes);
        var root = doc.RootElement;
        var chunksJson = root.GetProperty("chunks");
        var chunkCount = chunksJson.GetArrayLength();

        var queryEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(query);
        Log($"  Эмбеддинг запроса: {_embeddingProvider.Dimension} измерений");

        // Собираем все чанки с их расстояниями
        var idx = 0;
        var results = new List<(DocumentChunk Chunk, float Distance)>();

        await foreach (var embedding in _embeddingProvider.LoadEmbeddingsAsync(embeddingsPath))
        {
            var distance = CosineDistance(queryEmbedding, embedding);
            var chunkJson = chunksJson[idx];
            var chunk = CreateChunkFromJson(chunkJson);

            if (results.Count < topK * 2 || distance < results.Max(r => r.Distance))
            {
                results.Add((chunk, distance));
                if (results.Count > topK * 2)
                    results.RemoveAt(results.FindIndex(r => r.Distance == results.Max(x => x.Distance)));
            }

            idx++;
        }

        // Сортируем и берём топ-K
        results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return results.Take(topK).ToList();
    }

    private static DocumentChunk CreateChunkFromJson(JsonElement chunkJson) => new()
    {
        Id = chunkJson.GetProperty("id").GetString() ?? "",
        Text = chunkJson.GetProperty("text").GetString() ?? "",
        Metadata = new ChunkMetadata
        {
            Source = chunkJson.GetProperty("metadata").GetProperty("source").GetString() ?? "",
            Title = chunkJson.GetProperty("metadata").GetProperty("title").GetString() ?? "",
            Section = chunkJson.GetProperty("metadata").GetProperty("section").GetString() ?? "",
            ChunkId = chunkJson.GetProperty("metadata").GetProperty("chunk_id").GetString() ?? "",
        },
    };

    /// <summary>
    /// Сравнение стратегий (только метрики чанкинга, без эмбеддингов для всех).
    /// </summary>
    public async Task<(ChunkingComparisonResult Result1, ChunkingComparisonResult Result2)> CompareStrategiesAsync(
        IEnumerable<(string Text, string Source, string Title)> documents,
        IChunkingStrategy strategy1,
        IChunkingStrategy strategy2)
    {
        Log("=== Сравнение стратегий чанкинга ===");

        var result1 = await CompareSingleStrategyAsync(documents, strategy1);
        var result2 = await CompareSingleStrategyAsync(documents, strategy2);

        Log("=== Результаты ===");
        Log(result1.ToReport());
        Log(result2.ToReport());

        return (result1, result2);
    }

    private async Task<ChunkingComparisonResult> CompareSingleStrategyAsync(
        IEnumerable<(string Text, string Source, string Title)> documents,
        IChunkingStrategy strategy)
    {
        Log($"Стратегия: {strategy.Name}");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var chunks = new List<DocumentChunk>();

        foreach (var (text, source, title) in documents)
        {
            chunks.AddRange(strategy.Chunk(text, source, title));
        }

        var chunkingTime = sw.ElapsedMilliseconds;

        // Эмбеддинги только для образца (5 чанков)
        var sampleSize = Math.Min(5, chunks.Count);
        var sampleTexts = chunks.Take(sampleSize).Select(c => c.Text).ToList();

        var embSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _embeddingProvider.GenerateAndSaveEmbeddingsAsync(
                sampleTexts, Path.GetTempFileName());
        }
        catch (Exception ex)
        {
            Log($"  ⚠️  Эмбеддинги не сгенерированы: {ex.Message}");
        }
        var embeddingTime = embSw.ElapsedMilliseconds;

        var charCounts = chunks.Select(c => c.Text.Length).ToList();
        return new ChunkingComparisonResult
        {
            StrategyName = strategy.Name,
            ChunkCount = chunks.Count,
            AverageChunkSize = (float)charCounts.Average(),
            MinChunkSize = charCounts.Min(),
            MaxChunkSize = charCounts.Max(),
            TotalCharacters = charCounts.Sum(),
            ChunkingTimeMs = chunkingTime,
            EmbeddingTimeMs = embeddingTime,
        };
    }

    // Максимальный размер файла: 5 МБ для текста, 20 МБ для PDF
    private const long MaxTextFileSize = 5L * 1024 * 1024;
    private const long MaxPdfFileSize = 20L * 1024 * 1024;
    // Максимальный размер текста для эмбеддинга: 20 000 символов
    private const int MaxTextLength = 20000;

    private async Task<string> ExtractTextAsync(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var fileSize = new FileInfo(filePath).Length;

        // Проверяем размер файла
        if (ext == ".pdf" && fileSize > MaxPdfFileSize)
            throw new InvalidOperationException($"PDF слишком большой: {fileSize / 1024 / 1024}MB (макс: {MaxPdfFileSize / 1024 / 1024}MB)");
        if (ext != ".pdf" && fileSize > MaxTextFileSize)
            throw new InvalidOperationException($"Файл слишком большой: {fileSize / 1024 / 1024}MB (макс: {MaxTextFileSize / 1024 / 1024}MB)");

        return ext switch
        {
            ".pdf" => await ExtractPdfAsync(filePath),
            _ => await ReadTextFileAsync(filePath),
        };
    }

    /// <summary>
    /// Читает текстовый файл с ограничением размера.
    /// Не грузит весь файл, если он слишком большой.
    /// </summary>
    private static async Task<string> ReadTextFileAsync(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);

        // Читаем с ограничением — не больше MaxTextLength символов
        var sb = new StringBuilder();
        var buffer = new char[4096];
        int read;

        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            sb.Append(buffer, 0, read);
            if (sb.Length >= MaxTextLength)
            {
                // Обрезаем до целого слова
                sb.Length = Math.Min(MaxTextLength, sb.Length);
                var text = sb.ToString();
                int cutPos = text.LastIndexOfAny(new[] { ' ', '\t', '\n', '\r' }, text.Length - 1);
                if (cutPos > MaxTextLength / 2)
                    sb.Length = cutPos;
                break;
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Извлекает текст из PDF постранично.
    /// Обрабатывает только первые N страниц, чтобы не перегружать память.
    /// </summary>
    private static async Task<string> ExtractPdfAsync(string filePath)
    {
        const int maxPages = 50; // Ограничение на количество страниц
        const int maxPageSize = 5000; // Максимум символов на страницу

        // Пишем страницы напрямую в StringBuilder без промежуточного списка
        var sb = new StringBuilder();
        var firstPage = true;

        // Используем FileStream для потоковой загрузки
        await using var fs = File.OpenRead(filePath);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(fs);

        var pageCount = Math.Min(pdf.NumberOfPages, maxPages);

        for (var i = 1; i <= pageCount; i++)
        {
            var page = pdf.GetPage(i);
            var text = page.Text;

            if (!string.IsNullOrEmpty(text))
            {
                // Разделитель между страницами
                if (!firstPage)
                    sb.Append(new string('-', 80)).Append('\n');
                firstPage = false;

                // Триммируем страницу до лимита
                if (text.Length > maxPageSize)
                    text = text.Substring(0, maxPageSize);
                sb.Append(text);
            }

            // Освобождаем память после каждой страницы
            if (i % 10 == 0)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            }
        }

        return sb.ToString();
    }

    private static bool IsSupportedFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".pdf" or ".txt" or ".md" or ".cs" or ".py" or ".js" or ".json" or ".xml" or ".yaml" or ".yml" => true,
            _ => false,
        };
    }

    private static float CosineDistance(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 1.0f;
        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 1.0f;
        return 1.0f - (dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB)));
    }

    private static string SanitizeName(string name)
    {
        return new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
    }

    private static byte[] StripUtf8Bom(byte[] bytes)
    {
        // UTF-8 BOM: EF BB BF
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return bytes[3..];
        }
        return bytes;
    }

    private void Log(string message)
    {
        _logAction?.Invoke(message);
        Console.WriteLine(message);
    }
}
