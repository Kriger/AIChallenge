using AIChallenge.DocumentIndexing.Chunking;
using AIChallenge.DocumentIndexing.Embeddings;
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
    private readonly Action<string>? _logAction;

    public DocumentIndexer(IEmbeddingProvider embeddingProvider, Action<string>? logAction = null)
    {
        _embeddingProvider = embeddingProvider;
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
                var indexJsonPath = Path.Combine(outputDir, $"index_{chunkingStrategy.Name}_{safeName}.json");
                var embeddingsPath = Path.Combine(outputDir, $"embed_{chunkingStrategy.Name}_{safeName}.bin");

                // Проход 2: stream JSON + эмбеддинги
                var embStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var embProgress = new Progress<int>(completed =>
                    Log($"  Эмбеддинги: {completed}%"));

                await StreamIndexAsync(text, file, title, chunkingStrategy, indexJsonPath, embeddingsPath, embProgress);
                embStopwatch.Stop();

                var embSize = new FileInfo(embeddingsPath).Length;
                totalBytes += embSize;
                totalChunks += 1;

                Log($"  ✅ {embStopwatch.ElapsedMilliseconds} мс, {embSize / 1024} KB");
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
        Log($"  Размер эмбеддингов: {totalBytes / 1024 / 1024} MB");
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
        var indexJsonPath = Path.Combine(outputDir, $"index_{chunkingStrategy.Name}_{safeName}.json");
        var embeddingsPath = Path.Combine(outputDir, $"embed_{chunkingStrategy.Name}_{safeName}.bin");

        var embStopwatch = System.Diagnostics.Stopwatch.StartNew();
        await StreamIndexAsync(text, filePath, title, chunkingStrategy, indexJsonPath, embeddingsPath, null!);
        embStopwatch.Stop();

        var embSize = new FileInfo(embeddingsPath).Length;
        Log($"  ✅ {embStopwatch.ElapsedMilliseconds} мс, {embSize / 1024} KB");
        Log($"  📋 {indexJsonPath}");
        Log($"  💾 {embeddingsPath}");

        return true;
    }

    /// <summary>
    /// Stream-пишет JSON-индекс и эмбеддинги в ОДНОМ проходе по чанкам.
    /// </summary>
    private async Task StreamIndexAsync(
        string text,
        string source,
        string title,
        IChunkingStrategy chunkingStrategy,
        string indexJsonPath,
        string embeddingsPath,
        IProgress<int> embProgress)
    {
        await using var jsonFs = new FileStream(indexJsonPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var jsonW = new StreamWriter(jsonFs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 8192);
        var w = new JsonStreamWriter(jsonW);

        WriteIndexHeader(w, _embeddingProvider.ProviderName, _embeddingProvider.Dimension, chunkingStrategy.Name);

        await using var embFs = new FileStream(embeddingsPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var embBw = new BinaryWriter(embFs);
        embBw.Write(1); // version
        embBw.Write(0); // placeholder for dimension
        embBw.Write(0); // placeholder for count

        var chunkIndex = 0;
        var chunkCount = 0;
        var actualDimension = 0;

        foreach (var chunk in chunkingStrategy.Chunk(text, source, title))
        {
            chunkCount++;

            WriteChunkJson(w, chunk, chunkCount);

            try
            {
                var vector = await _embeddingProvider.GenerateEmbeddingAsync(chunk.Text);
                if (actualDimension == 0) actualDimension = vector.Length;
                foreach (var value in vector) embBw.Write(value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️  Ошибка эмбеддинга чанка {chunkIndex + 1}: {ex.Message}");
                for (var j = 0; j < (actualDimension > 0 ? actualDimension : 768); j++) embBw.Write(0f);
            }

            chunkIndex++;
            embProgress?.Report(chunkIndex);
        }

        w.WriteEndArray();
        w.WritePropertyName("chunk_count");
        w.WriteValue(chunkCount);

        embBw.BaseStream.Position = 4;
        embBw.Write(actualDimension);
        embBw.BaseStream.Position = 8;
        embBw.Write(chunkCount);
        embBw.BaseStream.Position = embBw.BaseStream.Length;
        w.WriteEndObject();
        await w.FlushAsync();
        embBw.Flush();
    }

    private static void WriteIndexHeader(JsonStreamWriter w, string providerName, int dimension, string strategyName)
    {
        w.WriteStartObject();
        w.WritePropertyName("version"); w.WriteValue("1.0.0");
        w.WritePropertyName("created_at"); w.WriteValue(DateTime.UtcNow.ToString("o"));
        w.WritePropertyName("embedding_provider"); w.WriteValue(providerName);
        w.WritePropertyName("embedding_dimension"); w.WriteValue(dimension);
        w.WritePropertyName("chunking_strategies");
        w.WriteStartArray();
        w.WriteValue(strategyName);
        w.WriteEndArray();
        w.WritePropertyName("chunks");
        w.WriteStartArray();
    }

    private static void WriteChunkJson(JsonStreamWriter w, DocumentChunk chunk, int chunkCount)
    {
        w.WriteStartObject();
        w.WritePropertyName("id"); w.WriteValue(chunk.Id);
        w.WritePropertyName("text"); w.WriteValue(chunk.Text);
        w.WritePropertyName("metadata");
        w.WriteStartObject();
        w.WritePropertyName("source"); w.WriteValue(chunk.Metadata.Source);
        w.WritePropertyName("title"); w.WriteValue(chunk.Metadata.Title);
        w.WritePropertyName("section"); w.WriteValue(chunk.Metadata.Section);
        w.WritePropertyName("chunk_id"); w.WriteValue(chunk.Metadata.ChunkId);
        w.WritePropertyName("chunk_index"); w.WriteValue(chunk.Metadata.ChunkIndex);
        w.WritePropertyName("total_chunks"); w.WriteValue(chunkCount);
        w.WritePropertyName("chunking_strategy"); w.WriteValue(chunk.Metadata.ChunkingStrategy);
        w.WritePropertyName("char_count"); w.WriteValue(chunk.Metadata.CharCount);
        w.WritePropertyName("created_at"); w.WriteValue(chunk.Metadata.CreatedAt.ToString("o"));
        w.WriteEndObject();
        w.WriteEndObject();
    }

    /// <summary>
    /// Простой stream-писатель JSON без внешних зависимостей.
    /// </summary>
    private class JsonStreamWriter
    {
        private readonly StreamWriter _w;
        private int _indent = 0;
        private bool _needsSeparator = false;

        public JsonStreamWriter(StreamWriter w) => _w = w;

        public void WriteStartObject()
        {
            if (_needsSeparator) _w.Write(',');
            WriteIndent();
            _w.Write('{');
            _indent++;
            _w.WriteLine();
            _needsSeparator = false;
        }

        public void WriteEndObject()
        {
            _w.WriteLine();
            _indent--;
            WriteIndent();
            _w.Write('}');
            _needsSeparator = true;
        }

        public void WriteStartArray()
        {
            if (_needsSeparator) _w.Write(',');
            WriteIndent();
            _w.Write('[');
            _indent++;
            _w.WriteLine();
            _needsSeparator = false;
        }

        public void WriteEndArray()
        {
            _w.WriteLine();
            _indent--;
            WriteIndent();
            _w.Write(']');
            _needsSeparator = true;
        }

        public void WritePropertyName(string name)
        {
            if (_needsSeparator) _w.Write(',');
            _w.Write('"');
            _w.Write(EscapeJson(name));
            _w.Write("\": ");
            _needsSeparator = false;
        }

        public void WriteValue(string value)
        {
            _w.Write('"');
            _w.Write(EscapeJson(value));
            _w.Write('"');
            _needsSeparator = true;
        }

        public void WriteValue(int value)
        {
            _w.Write(value);
            _needsSeparator = true;
        }
        public void WriteValue(long value)
        {
            _w.Write(value);
            _needsSeparator = true;
        }
        public void WriteValue(double value)
        {
            _w.Write(value);
            _needsSeparator = true;
        }
        public void WriteValue(bool value)
        {
            _w.Write(value);
            _needsSeparator = true;
        }

        public Task FlushAsync() => _w.FlushAsync();

        private void WriteIndent()
        {
            for (var i = 0; i < _indent; i++)
                _w.Write("  ");
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                sb.Append(c switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => c
                });
            }
            return sb.ToString();
        }
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

        using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(indexJsonPath));
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

    private void Log(string message)
    {
        _logAction?.Invoke(message);
        Console.WriteLine(message);
    }
}
