using AIChallenge.Cli.Commands;
using AIChallenge.DocumentIndexing;
using AIChallenge.DocumentIndexing.Embeddings;
using AIChallenge.DocumentIndexing.Models;
using AIChallenge.McpScheduler;
using AIChallenge.Models;
using System.Text;
using System.Text.Json;

namespace AIChallenge.Cli.Commands;

/// <summary>
/// RAG-команда: вопрос → поиск чанков → объединение с вопросом → запрос к LLM.
/// Сравнивает ответ модели без RAG и с RAG.
/// </summary>
public class RagCommand : CommandHandler
{
    public override string Name => "rag";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            Console.WriteLine("❌ Укажите запрос: /rag <question>");
            Console.WriteLine("   Пример: /rag Как работает ContextManager?");
            Console.WriteLine();
            Console.WriteLine("📋 Как работает RAG:");
            Console.WriteLine("  1. Вопрос отправляется в индекс документов для поиска похожих чанков");
            Console.WriteLine("  2. Топ-K релевантных чанков извлекаются из проиндексированных файлов");
            Console.WriteLine("  3. Ответ БЕЗ RAG: вопрос → LLM (без контекста из документов)");
            Console.WriteLine("  4. Ответ С RAG: вопрос + чанки → LLM (с контекстом из документов)");
            Console.WriteLine();
            return true;
        }

        var question = string.Join(" ", parts.Skip(2));
        var indexDir = GetIndexDir(ctx);
        var indexFiles = GetIndexFiles(indexDir);

        if (indexFiles.Count == 0)
        {
            Console.WriteLine("❌ Индексы не найдены. Сначала проиндексируйте документы:");
            Console.WriteLine("   /doc index <path> [chunking_strategy]");
            return true;
        }

        Console.WriteLine("🔍 Шаг 1/4: Поиск чанков...");
        var searchResults = await SearchAllIndexesAsync(question, indexDir, indexFiles, ctx);

        // Fallback: если семантический поиск не нашёл ничего, ищем по ключевым словам
        if (searchResults.Count == 0)
        {
            Console.WriteLine("  Семантический поиск не дал результатов, пробую поиск по ключевым словам...");
            searchResults = await SearchByKeywordsAsync(question, indexDir, indexFiles, ctx);
            Console.WriteLine($"  Ключевые слова: {string.Join(", ", ExtractKeywords(question))}");
        }

        if (searchResults.Count == 0)
        {
            Console.WriteLine("⚠️  Результаты поиска пустые.");
            return true;
        }

        // Сортируем по схожести (по убыванию)
        searchResults.Sort((a, b) => b.Distance.CompareTo(a.Distance));

        Console.WriteLine($"   Найдено {searchResults.Count} чанков\n");

        // Формируем контекст из чанков
        var ragContext = BuildRagContext(searchResults);

        Console.WriteLine("📋 Шаг 2/4: Ответ БЕЗ RAG (вопрос → LLM)...");
        var answerWithoutRag = await CallLlmAsync(
            ctx.Config,
            question,
            "Ты — полезный ассистент. Отвечай на вопрос пользователя, используя свои внутренние знания.",
            question,
            temperature: 0.3);

        // Показываем, какие чанки попали в контекст
        Console.WriteLine("📦 Контекст для RAG (найденные чанки):");
        Console.WriteLine(new string('-', 80));
        foreach (var (chunk, distance, strategy, safeName) in searchResults)
        {
            var similarity = 1.0f - distance;
            Console.WriteLine($"  [Схожесть: {similarity:F3}] [{safeName}] [{strategy}]");
            Console.WriteLine($"  {chunk.Text}");
            Console.WriteLine();
        }
        Console.WriteLine(new string('-', 80));
        Console.WriteLine();

        Console.WriteLine("📚 Шаг 3/4: Ответ С RAG (вопрос + чанки → LLM)...");
        var ragPrompt = $"""
            === РОЛЬ ===
            Ты — аналитик, который отвечает на вопросы, основываясь ТОЛЬКО на предоставленных документах.

            === СТРОГИЕ ПРАВИЛА ===
            1. ОТВЕЧАЙ ТОЛЬКО на основе информации из документов ниже.
            2. НЕ используй свои внешние знания. НЕ добавляй информацию, которой нет в документах.
            3. Если ответ есть в документах — приведи его.
            4. Если ответ есть частично — скажи, что именно найдено.
            5. Если документы НЕ содержат ответа на вопрос — напиши: "В предоставленных документах нет информации об этом."

            === ВАЖНО ===
            Если в документах ЕСТЬ ответ — ты ОБЯЗАН его использовать.
            Если ты напишешь "нет информации", хотя ответ есть в документах — это ОШИБКА.

            === ДОКУМЕНТЫ ===
            {ragContext}
            === КОНЕЦ ДОКУМЕНТОВ ===

            === ВОПРОС ===
            {question}

            === ОТВЕТ ===
            """;

        var answerWithRag = await CallLlmAsync(
            ctx.Config,
            ragPrompt,
            "Ты — аналитик, отвечающий строго по документам.",
            question,
            temperature: 0.1,
            maxTokens: 4096);

        // Вывод результатов
        PrintRagComparison(question, searchResults, answerWithoutRag, answerWithRag);

        return true;
    }

    private string GetIndexDir(CommandContext ctx)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, ctx.DocIndexConfig.IndexSubDir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private List<string> GetIndexFiles(string indexDir)
        => Directory.GetFiles(indexDir, "index_*.json")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();

    private async Task<List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)>> SearchAllIndexesAsync(
        string query, string indexDir, List<string> indexFiles, CommandContext ctx)
    {
        var embeddingProvider = new OllamaEmbeddingProvider(
            ctx.DocIndexConfig.OllamaUrl, ctx.DocIndexConfig.OllamaEmbeddingModel);

        try
        {
            var allResults = new List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)>();
            var indexer = new DocumentIndexer(embeddingProvider);

            // Увеличиваем topK для лучшего покрытия — ищем больше чанков на индекс
            var topKPerIndex = Math.Max(ctx.DocIndexConfig.SearchTopK * 5, 25);

            foreach (var jsonFile in indexFiles)
            {
                var (storage, strategy, safeName) = ParseIndexFileName(jsonFile);
                var binFile = FindBinFile(indexDir, storage, strategy, safeName);
                if (binFile == null) continue;

                var results = await indexer.SearchAsync(
                    query, jsonFile, binFile,
                    topK: topKPerIndex, strategy);

                foreach (var (chunk, distance, strat) in results)
                {
                    allResults.Add((chunk, distance, strat, safeName));
                }
            }

            // Берём топ-K уникальных по тексту чанков (убираем дубликаты от overlap)
            var seenTexts = new HashSet<string>();
            var deduped = allResults
                .Where(r => seenTexts.Add(r.Chunk.Text))
                .Take(ctx.DocIndexConfig.SearchTopK * 3)
                .ToList();

            return deduped;
        }
        finally
        {
            embeddingProvider.Dispose();
        }
    }

    private static (string storage, string strategy, string safeName) ParseIndexFileName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var rest = baseName.Substring("index_".Length);

        var firstIdx = rest.IndexOf('_');
        if (firstIdx <= 0) return (rest, "", "");

        var storage = rest.Substring(0, firstIdx);
        var rest2 = rest.Substring(firstIdx + 1);

        var knownStrategies = new[] { "fixed_size", "structural" };

        foreach (var known in knownStrategies)
        {
            var prefix = known + "_";
            if (rest2.StartsWith(prefix, StringComparison.Ordinal))
            {
                var safeName = rest2.Substring(prefix.Length);
                return (storage, known, safeName);
            }
        }

        var secondIdx = rest2.IndexOf('_');
        var strategy = secondIdx > 0 ? rest2.Substring(0, secondIdx) : rest2;
        var safeNameFallback = secondIdx > 0 ? rest2.Substring(secondIdx + 1) : "";
        return (storage, strategy, safeNameFallback);
    }

    private static string? FindBinFile(string indexDir, string storage, string strategy, string safeName)
        => Directory.GetFiles(indexDir, $"index_{storage}_{strategy}_{safeName}.bin").FirstOrDefault();

    /// <summary>
    /// Fallback-поиск по ключевым словам: загружает все чанки из индексов,
    /// фильтрует по наличию ключевых слов в тексте.
    /// </summary>
    private async Task<List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)>> SearchByKeywordsAsync(
        string query, string indexDir, List<string> indexFiles, CommandContext ctx)
    {
        var results = new List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)>();

        // Извлекаем ключевые слова из запроса
        var keywords = ExtractKeywords(query);
        if (keywords.Count == 0) return results;

        foreach (var jsonFile in indexFiles)
        {
            var (storage, strategy, safeName) = ParseIndexFileName(jsonFile);

            try
            {
                using var doc = ParseJsonWithBomHandling(jsonFile);
                var root = doc.RootElement;
                var chunksJson = root.GetProperty("chunks");
                var chunkCount = chunksJson.GetArrayLength();

                for (var i = 0; i < chunkCount; i++)
                {
                    var chunkJson = chunksJson[i];
                    var text = chunkJson.GetProperty("text").GetString() ?? "";
                    var source = chunkJson.GetProperty("metadata").GetProperty("source").GetString() ?? "";

                    // Считаем, сколько ключевых слов найдено в чанке
                    var keywordMatches = keywords.Count(kw =>
                        text.Contains(kw, StringComparison.OrdinalIgnoreCase));

                    if (keywordMatches > 0)
                    {
                        // Вычисляем score: чем больше ключевых слов найдено, тем выше score
                        var score = Math.Min(1.0f, keywordMatches * 0.3f);
                        var distance = 1.0f - score;

                        var chunk = new DocumentChunk
                        {
                            Id = chunkJson.GetProperty("id").GetString() ?? "",
                            Text = text,
                            Metadata = new ChunkMetadata
                            {
                                Source = source,
                                Title = chunkJson.GetProperty("metadata").GetProperty("title").GetString() ?? "",
                                Section = chunkJson.GetProperty("metadata").GetProperty("section").GetString() ?? "",
                                ChunkId = chunkJson.GetProperty("metadata").GetProperty("chunk_id").GetString() ?? "",
                            },
                        };

                        results.Add((chunk, distance, strategy, safeName));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️  Ошибка чтения {jsonFile}: {ex.Message}");
            }
        }

        // Сортируем по score (по убыванию) и берём топ-K
        results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return results.Take(ctx.DocIndexConfig.SearchTopK * 3).ToList();
    }

    private static List<string> ExtractKeywords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "какие", "какая", "какой", "что", "как", "где", "почему", "когда",
            "в", "из", "с", "на", "и", "или", "не", "к", "у", "о", "при",
            "three", "what", "how", "where", "why", "when", "which", "are",
            "the", "a", "an", "is", "are", "was", "were", "of", "in", "to",
            "for", "with", "on", "at", "by", "from", "as", "into", "through"
        };

        var words = text.ToLowerInvariant()
            .Replace("?", " ")
            .Replace("!", " ")
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct()
            .ToList();

        return words;
    }

    private JsonDocument ParseJsonWithBomHandling(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        bytes = StripUtf8Bom(bytes);
        return JsonDocument.Parse(bytes);
    }

    private static byte[] StripUtf8Bom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return bytes[3..];
        return bytes;
    }

    private static string BuildRagContext(List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)> results)
    {
        var sb = new StringBuilder();

        // Сортируем по схожести (по убыванию)
        var sorted = results
            .OrderByDescending(r => 1.0f - r.Distance)
            .ToList();

        foreach (var (chunk, distance, strategy, safeName) in sorted)
        {
            var similarity = 1.0f - distance;
            sb.AppendLine($"--- Документ: {safeName} | Стратегия: {strategy} | Схожесть: {similarity:F3} ---");

            if (!string.IsNullOrWhiteSpace(chunk.Metadata.Section))
            {
                sb.AppendLine($"Раздел: {chunk.Metadata.Section}");
            }

            sb.AppendLine(chunk.Text);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<string> CallLlmAsync(GigaChatConfig config, string systemPrompt, string userPrompt, string displayQuestion, double? temperature = null, int? maxTokens = null)
    {
        var client = new GigaChatClient(config.ClientId, config.ClientSecret);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var answer = await client.ChatAsync(
            model: config.Model,
            systemPrompt: systemPrompt,
            userPrompt: userPrompt,
            temperature: temperature ?? 0.3,
            maxTokens: maxTokens ?? (config.MaxTokens > 0 ? config.MaxTokens : 2000));
        stopwatch.Stop();

        return answer;
    }

    private static void PrintRagComparison(
        string question,
        List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)> searchResults,
        string answerWithoutRag,
        string answerWithRag)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"📝 ВОПРОС: {question}");
        Console.WriteLine(new string('=', 80));
        Console.WriteLine();

        // Поиск — релевантные чанки
        Console.WriteLine("🔎 РЕЗУЛЬТАТЫ ПОИСКА:");
        Console.WriteLine(new string('-', 60));

        var grouped = searchResults
            .GroupBy(r => r.SafeName)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var group in grouped)
        {
            Console.WriteLine($"\n  📁 {group.Key}");

            foreach (var (chunk, distance, strategy, _) in group)
            {
                var similarity = 1.0f - distance;
                var preview = chunk.Text.Length > 200 ? chunk.Text[..200] + "..." : chunk.Text;
                Console.WriteLine($"    [{strategy,-12}] {similarity:F3} | {preview}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(new string('=', 80));
        Console.WriteLine("🤖 ОТВЕТ БЕЗ RAG (только вопрос → LLM):");
        Console.WriteLine(new string('-', 80));
        Console.WriteLine(answerWithoutRag);
        Console.WriteLine(new string('=', 80));
        Console.WriteLine();
        Console.WriteLine("📚 ОТВЕТ С RAG (вопрос + релевантные чанки → LLM):");
        Console.WriteLine(new string('-', 80));
        Console.WriteLine(answerWithRag);
        Console.WriteLine(new string('=', 80));

        // Краткое сравнение
        Console.WriteLine();
        PrintComparisonSummary(answerWithoutRag, answerWithRag);
    }

    private static void PrintComparisonSummary(string answerWithoutRag, string answerWithRag)
    {
        Console.WriteLine("📊 СРАВНЕНИЕ:");
        var wordsWithout = CountWords(answerWithoutRag);
        var wordsWith = CountWords(answerWithRag);
        Console.WriteLine($"  Без RAG: {answerWithoutRag.Length} символов, {wordsWithout} слов");
        Console.WriteLine($"  С RAG:   {answerWithRag.Length} символов, {wordsWith} слов");

        // Простая эвристика: если ответы существенно различаются
        var withoutTrimmed = answerWithoutRag.Trim();
        var withTrimmed = answerWithRag.Trim();

        if (withoutTrimmed == withTrimmed)
        {
            Console.WriteLine("  ⚠️  Ответы идентичны — RAG не повлиял на результат");
        }
        else if (withTrimmed.Length > withoutTrimmed.Length * 1.3)
        {
            Console.WriteLine("  ✅ RAG значительно расширил ответ (больше деталей из документов)");
        }
        else if (withTrimmed.Length < withoutTrimmed.Length * 0.7)
        {
            Console.WriteLine("  ✅ RAG сузил ответ (более точный, основанный на документах)");
        }
        else
        {
            Console.WriteLine("  ✅ RAG изменил ответ (добавлены/изменены детали)");
        }
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length;
    }
}
