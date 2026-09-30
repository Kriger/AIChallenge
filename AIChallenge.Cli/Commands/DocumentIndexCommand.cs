using AIChallenge.Cli.Commands;
using AIChallenge.DocumentIndexing;
using AIChallenge.DocumentIndexing.Chunking;
using AIChallenge.DocumentIndexing.Embeddings;
using AIChallenge.DocumentIndexing.Models;
using AIChallenge.Models;
using System.Linq;
using System.Text.Json;

namespace AIChallenge.Cli.Commands;

/// <summary>
/// Команда для индексации документов.
/// Потоковая обработка: один файл за раз, память не растёт.
/// </summary>
public class DocumentIndexCommand : CommandHandler
{
    public override string Name => "doc";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            ShowHelp(ctx.DocIndexConfig);
            return true;
        }

        var subCommand = parts[1].ToLowerInvariant();

        switch (subCommand)
        {
            case "index": return await IndexDocumentsAsync(parts, ctx);
            case "compare": return await CompareStrategiesAsync(parts, ctx);
            case "search": return await SearchDocumentsAsync(parts, ctx);
            case "vectors": return await ShowVectorsAsync(parts, ctx);
            case "list": return await ListDocumentsAsync(parts, ctx);
            case "help":
            default:
                ShowHelp(ctx.DocIndexConfig);
                return true;
        }
    }

    private OllamaEmbeddingProvider CreateEmbeddingProvider(CommandContext ctx)
        => new(ctx.DocIndexConfig.OllamaUrl, ctx.DocIndexConfig.OllamaEmbeddingModel);

    private string GetIndexDir(CommandContext ctx)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, ctx.DocIndexConfig.IndexSubDir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Разбирает имя файла индекса: index_{strategy}_{safeName}.json → (strategy, safeName).
    /// </summary>
    private static (string strategy, string safeName) ParseIndexFileName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var rest = baseName.Substring("index_".Length);
        var idx = rest.IndexOf('_');
        var strategy = idx > 0 ? rest.Substring(0, idx) : rest;
        var safeName = idx > 0 ? rest.Substring(idx + 1) : "";
        return (strategy, safeName);
    }

    private static List<string> GetJsonFiles(string indexDir)
        => Directory.GetFiles(indexDir, "index_*.json")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();

    private static string? FindBinFile(string indexDir, string strategy, string safeName)
        => Directory.GetFiles(indexDir, $"embed_{strategy}_{safeName}.bin").FirstOrDefault();

    private void ShowHelp(DocumentIndexingConfig cfg)
    {
        Console.WriteLine($"""
            📄 Команды индексации документов:

            /doc index <path> [strategy]
              - Индексировать документ или папку (потоково, память не растёт)
              - path: путь к файлу или папке с документами
              - strategy: fixed_size или structural (по умолчанию: structural)
              - Форматы: PDF, TXT, MD, CS, PY, JS, JSON, XML, YAML
              - Эмбеддинги: Ollama ({cfg.OllamaEmbeddingModel})

            /doc compare <path>
              - Сравнить стратегии чанкинга

            /doc search <query>
              - Поиск по проиндексированным документам (топ {cfg.SearchTopK}, порог {cfg.MinSimilarity:F2})

            /doc vectors [doc_name]
              - Показать эмбеддинги (все или одного документа)

            /doc list
              - Показать список проиндексированных документов

            /doc help
              - Показать справку
            """);
    }

    private async Task<bool> IndexDocumentsAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            Console.WriteLine("❌ Укажите путь: /doc index <path> [strategy]");
            return true;
        }

        var path = parts[2];
        var strategyName = parts.Length > 3 ? parts[3].ToLowerInvariant() : "structural";

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Console.WriteLine($"❌ Путь не найден: {path}");
            return true;
        }

        var indexDir = GetIndexDir(ctx);
        var embeddingProvider = CreateEmbeddingProvider(ctx);

        try
        {
            var indexer = new DocumentIndexer(embeddingProvider);

            IChunkingStrategy strategy = strategyName switch
            {
                "fixed" or "fixed_size" => new FixedSizeChunking(
                    chunkSize: ctx.DocIndexConfig.ChunkSize,
                    overlap: ctx.DocIndexConfig.ChunkOverlap),
                "structural" or "structure" => new StructuralChunking(),
                _ => new StructuralChunking(),
            };

            Console.WriteLine($"📚 Путь: {path}");
            Console.WriteLine($"🔧 Стратегия: {strategy.Name}");
            Console.WriteLine($"💾 Сохранение: {indexDir}");
            Console.WriteLine();

            if (File.Exists(path))
            {
                Console.WriteLine($"[1/1] {Path.GetFileName(path)}...");
                var ok = await indexer.IndexFileAsync(path, strategy, indexDir);
                Console.WriteLine();
                Console.WriteLine(ok ? "✅ Индексация завершена!" : "❌ Ошибка индексации");
            }
            else
            {
                var totalFiles = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Count(f => IsSupportedFile(f, ctx.DocIndexConfig));

                Console.WriteLine($"📁 Папка: {path}");
                Console.WriteLine($"📄 Файлов: {totalFiles}");
                Console.WriteLine($"⚡ Потоковая обработка: память не растёт");
                Console.WriteLine();

                await indexer.IndexDirectoryAsync(
                    path, strategy, indexDir, recursive: true,
                    progressCallback: (current, total) =>
                        Console.WriteLine($"  Прогресс: {current}/{total} ({current * 100 / total}%)"));

                Console.WriteLine("\n✅ Индексация завершена!");
            }
        }
        finally
        {
            embeddingProvider.Dispose();
        }

        return true;
    }

    private async Task<bool> CompareStrategiesAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            Console.WriteLine("❌ Укажите путь: /doc compare <path>");
            return true;
        }

        var path = parts[2];
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Console.WriteLine($"❌ Путь не найден: {path}");
            return true;
        }

        var indexDir = GetIndexDir(ctx);
        var embeddingProvider = CreateEmbeddingProvider(ctx);

        try
        {
            var indexer = new DocumentIndexer(embeddingProvider);

            var documents = new List<(string Text, string Source, string Title)>();
            var maxFiles = ctx.DocIndexConfig.CompareMaxFiles;

            if (File.Exists(path))
            {
                var text = await ReadTextFileAsync(path, ctx.DocIndexConfig);
                documents.Add((text, path, Path.GetFileNameWithoutExtension(path)));
            }
            else
            {
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsSupportedFile(f, ctx.DocIndexConfig)).Take(maxFiles).ToList();

                foreach (var file in files)
                {
                    var text = await ReadTextFileAsync(file, ctx.DocIndexConfig);
                    documents.Add((text, file, Path.GetFileNameWithoutExtension(file)));
                }
            }

            if (documents.Count == 0)
            {
                Console.WriteLine("❌ Не удалось прочитать документы");
                return true;
            }

            var (r1, r2) = await indexer.CompareStrategiesAsync(
                documents, new FixedSizeChunking(500, 50), new StructuralChunking());
            Console.WriteLine("\n✅ Сравнение завершено!");

            return true;
        }
        finally
        {
            embeddingProvider.Dispose();
        }
    }

    private async Task<bool> SearchDocumentsAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            Console.WriteLine("❌ Укажите запрос: /doc search <query>");
            return true;
        }

        var query = string.Join(" ", parts.Skip(2));
        var indexDir = GetIndexDir(ctx);
        var jsonFiles = GetJsonFiles(indexDir);

        if (jsonFiles.Count == 0)
        {
            Console.WriteLine("❌ Индексы не найдены.");
            return true;
        }

        var allResults = new List<(DocumentChunk Chunk, float Distance)>();
        var embeddingProvider = CreateEmbeddingProvider(ctx);

        try
        {
            var indexer = new DocumentIndexer(embeddingProvider);

            foreach (var jsonFile in jsonFiles)
            {
                var (strategy, safeName) = ParseIndexFileName(jsonFile);
                var binFile = FindBinFile(indexDir, strategy, safeName);
                if (binFile == null) continue;

                var results = await indexer.SearchAsync(query, jsonFile, binFile, topK: ctx.DocIndexConfig.SearchTopK);
                allResults.AddRange(results);
            }

            allResults.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            allResults = allResults
                .Where(r => (1.0f - r.Distance) >= ctx.DocIndexConfig.MinSimilarity)
                .Take(ctx.DocIndexConfig.SearchTopK)
                .ToList();

            Console.WriteLine($"\n=== Результаты поиска ===\n  Порог схожести: {ctx.DocIndexConfig.MinSimilarity:F2}\n");

            if (allResults.Count == 0)
            {
                Console.WriteLine($"  Ничего не найдено (нет результатов с схожестью >= {ctx.DocIndexConfig.MinSimilarity})");
                return true;
            }

            for (var i = 0; i < allResults.Count; i++)
            {
                var (chunk, distance) = allResults[i];
                var similarity = 1.0f - distance;
                var preview = chunk.Text.Length > 200 ? chunk.Text[..200] + "..." : chunk.Text;

                Console.WriteLine($"#{i + 1} (Схожесть: {similarity:F3})");
                Console.WriteLine($"   Источник: {chunk.Metadata.Source}");
                Console.WriteLine($"   Раздел: {chunk.Metadata.Section}");
                Console.WriteLine($"   Текст: {preview}");
                Console.WriteLine();
            }
        }
        finally
        {
            embeddingProvider.Dispose();
        }

        return true;
    }

    private async Task<bool> ShowVectorsAsync(string[] parts, CommandContext ctx)
    {
        var indexDir = GetIndexDir(ctx);
        var jsonFiles = GetJsonFiles(indexDir);

        if (jsonFiles.Count == 0)
        {
            Console.WriteLine("❌ Индексы не найдены.");
            return true;
        }

        var filter = parts.Length > 2 ? parts[2].ToLowerInvariant() : null;
        var embeddingProvider = CreateEmbeddingProvider(ctx);

        try
        {
            foreach (var jsonFile in jsonFiles)
            {
                var baseName = Path.GetFileNameWithoutExtension(jsonFile);
                if (filter != null && !baseName.ToLowerInvariant().Contains(filter))
                    continue;

                Console.WriteLine($"📁 {baseName}");

                using var doc = JsonDocument.Parse(File.ReadAllBytes(jsonFile));
                var root = doc.RootElement;
                var chunksJson = root.GetProperty("chunks");
                var chunkCount = chunksJson.GetArrayLength();
                var dimension = root.GetProperty("embedding_dimension").GetInt32();
                var provider = root.TryGetProperty("embedding_provider", out var prov)
                    ? prov.GetString() ?? "?" : "?";

                Console.WriteLine($"  Провайдер: {provider}, Размерность: {dimension}, Чанков: {chunkCount}");

                var (strategy, safeName) = ParseIndexFileName(jsonFile);
                var binFile = FindBinFile(indexDir, strategy, safeName);
                if (binFile == null)
                {
                    Console.WriteLine("  ⚠️  Эмбеддинги не найдены\n");
                    continue;
                }

                var chunkIndex = 0;
                await foreach (var vector in embeddingProvider.LoadEmbeddingsAsync(binFile).ConfigureAwait(false))
                {
                    if (chunkIndex >= chunkCount) break;

                    var chunkJson = chunksJson[chunkIndex];
                    var text = chunkJson.GetProperty("text").GetString() ?? "";
                    var shortText = text.Length > 80 ? text[..80] + "..." : text;
                    var preview = string.Join(", ", vector.Take(5).Select(v => v.ToString("F4")));

                    Console.WriteLine($"  [{chunkIndex}] dist={vector.Length} preview=[{preview}, ...]");
                    Console.WriteLine($"       \"{shortText}\"");
                    chunkIndex++;
                }

                Console.WriteLine();
            }
        }
        finally
        {
            embeddingProvider.Dispose();
        }

        return true;
    }

    private async Task<bool> ListDocumentsAsync(string[] parts, CommandContext ctx)
    {
        var indexDir = GetIndexDir(ctx);
        var jsonFiles = GetJsonFiles(indexDir);

        if (jsonFiles.Count == 0)
        {
            Console.WriteLine("❌ Индексы не найдены.");
            return true;
        }

        Console.WriteLine("=== Проиндексированные документы ===\n");

        foreach (var jsonFile in jsonFiles)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(jsonFile));
                var root = doc.RootElement;

                var chunkCount = root.GetProperty("chunk_count").GetInt32();
                var strategy = root.GetProperty("chunking_strategies")
                    .EnumerateArray().FirstOrDefault().GetString() ?? "unknown";
                var createdAt = root.GetProperty("created_at").GetString() ?? "unknown";

                var (s, safeName) = ParseIndexFileName(jsonFile);
                var binFile = FindBinFile(indexDir, s, safeName);
                var binSize = binFile is not null && File.Exists(binFile)
                    ? $" ({new FileInfo(binFile).Length / 1024} KB)"
                    : " (эмбеддинги отсутствуют)";

                Console.WriteLine($"📁 {Path.GetFileNameWithoutExtension(jsonFile)}");
                Console.WriteLine($"   Стратегия: {strategy}");
                Console.WriteLine($"   Чанков: {chunkCount}");
                Console.WriteLine($"   Размер эмбеддингов: {binSize}");
                Console.WriteLine($"   Создан: {createdAt}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Ошибка чтения {Path.GetFileName(jsonFile)}: {ex.Message}\n");
            }
        }

        return true;
    }

    private static async Task<string> ReadTextFileAsync(string filePath, DocumentIndexingConfig cfg)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: cfg.ReadTextBufferSize, leaveOpen: false);

        var sb = new StringBuilder();
        var buffer = new char[cfg.ReadTextBufferSize];
        int read;
        var maxLen = cfg.ReadTextMaxLength;

        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            sb.Append(buffer, 0, read);
            if (sb.Length >= maxLen)
            {
                sb.Length = Math.Min(maxLen, sb.Length);
                var text = sb.ToString();
                int cutPos = text.LastIndexOfAny(new[] { ' ', '\t', '\n', '\r' }, text.Length - 1);
                if (cutPos > maxLen / 2)
                    sb.Length = cutPos;
                break;
            }
        }

        return sb.ToString().Trim();
    }

    private static bool IsSupportedFile(string filePath, DocumentIndexingConfig cfg)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext switch
        {
            ".pdf" or ".txt" or ".md" or ".cs" or ".py" or ".js" or ".json" or ".xml" or ".yaml" or ".yml" => true,
            _ => false,
        } is not true)
            return false;

        // Фильтруем большие файлы
        try
        {
            var info = new FileInfo(filePath);
            var size = info.Length;
            if (ext == ".pdf" && size > cfg.MaxPdfFileSize)
            {
                Console.WriteLine($"  ⏭️  Пропуск: {info.Name} ({size / 1024 / 1024}MB > {cfg.MaxPdfFileSize / 1024 / 1024}MB)");
                return false;
            }
            if (ext != ".pdf" && size > cfg.MaxTextFileSize)
            {
                Console.WriteLine($"  ⏭️  Пропуск: {info.Name} ({size / 1024 / 1024}MB > {cfg.MaxTextFileSize / 1024 / 1024}MB)");
                return false;
            }
        }
        catch { }

        return true;
    }
}