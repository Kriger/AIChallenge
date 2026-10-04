using AIChallenge.Cli.Commands;
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

        Console.WriteLine("🔍 Шаг 1/3: Поиск чанков...");
        var searchResults = await SearchAllIndexesAsync(question, indexDir, indexFiles, ctx);

        if (searchResults.Count == 0)
        {
            Console.WriteLine("⚠️  Результаты поиска пустые.");
            return true;
        }

        Console.WriteLine($"   Найдено {searchResults.Count} чанков\n");

        // ===== Этап 2: Reranker / фильтр релевантности =====
        Console.WriteLine("🎯 Шаг 2/3: Фильтрация релевантности...");
        var expandFactor = ctx.DocIndexConfig.RagExpandFactor;
        var minSimilarity = ctx.DocIndexConfig.RagMinRelevanceScore;
        var reRankTopK = ctx.DocIndexConfig.RagReRankTopK;

        Console.WriteLine($"   ExpandFactor: {expandFactor} | Порог: {minSimilarity:F2} | Top-K после фильтра: {reRankTopK}");

        var (filteredResults, rejectedResults, totalBefore, totalAfter, minKeptSim, maxRejectSim) =
            RerankAndFilter(searchResults, minSimilarity, reRankTopK);

        if (ctx.DocIndexConfig.RagShowFilterStats && rejectedResults.Count > 0)
        {
            Console.WriteLine($"   ✅ Отобрано: {totalAfter} чанков (из {totalBefore})");
            Console.WriteLine($"   ❌ Отброшено: {rejectedResults.Count} чанков (similarity < {minSimilarity:F2})");

            if (maxRejectSim.HasValue)
                Console.WriteLine($"   📉 Макс. similarity среди отброшенных: {maxRejectSim.Value:F3}");
            if (minKeptSim.HasValue)
                Console.WriteLine($"   📈 Мин. similarity среди отобранных: {minKeptSim.Value:F3}");
            Console.WriteLine();
        }

        if (filteredResults.Count == 0)
        {
            Console.WriteLine("⚠️  После фильтрации чанки не найдены.");
            return true;
        }

        // ===== Проверка порога релевантности для формирования ответа =====
        var maxSimilarity = filteredResults.Max(r => 1.0f - r.Distance);
        var answerThreshold = ctx.DocIndexConfig.RagAnswerThreshold;

        if (maxSimilarity < answerThreshold)
        {
            Console.WriteLine($"⚠️  Максимальная релевантность чанков ({maxSimilarity:F3}) ниже порога ({answerThreshold:F2}).");
            Console.WriteLine("   Ассистент не может ответить на основе предоставленных документов.");
            Console.WriteLine("   Пожалуйста, уточните ваш запрос или попробуйте другие ключевые слова.");
            return true;
        }

        // Формируем контекст из отфильтрованных чанков
        var ragContext = BuildRagContext(filteredResults);

        // Показываем, какие чанки нашлись (диагностика)
        Console.WriteLine("📦 Найденные чанки (схожесть, документ, текст):");
        Console.WriteLine(new string('-', 80));
        foreach (var (chunk, distance, strategy, safeName) in searchResults)
        {
            var similarity = 1.0f - distance;
            var preview = chunk.Text.Length > 150 ? chunk.Text[..150] + "..." : chunk.Text;
            Console.WriteLine($"  [{similarity:F3}] [{safeName}] [{strategy}]");
            Console.WriteLine($"    {preview}");
            Console.WriteLine();
        }
        Console.WriteLine(new string('-', 80));
        Console.WriteLine();

        Console.WriteLine("📋 Шаг 3/4: Ответ БЕЗ RAG (вопрос → LLM)...");
        var answerWithoutRag = await CallLlmAsync(
            ctx.Config,
            question,
            "Ты — полезный ассистент. Отвечай на вопрос пользователя, используя свои внутренние знания.",
            question,
            temperature: 0.3);

        Console.WriteLine("📚 Шаг 4/4: Ответ С RAG (вопрос + чанки → LLM)...");
        var ragPrompt = $"""
            === РОЛЬ ===
            Ты -- аналитик, который отвечает на вопросы на основе предоставленных документов.

            === ИНСТРУКЦИИ ===
            1. Внимательно прочитай все документы (чанки) ниже.
            2. Найди информацию, которая отвечает на вопрос пользователя.
            3. Ответь на вопрос, используя информацию из документов.
            4. Если ответ есть в документах -- приведи его подробно.
            5. Если ответ есть частично -- укажи, что именно найдено.
            6. Если документы НЕ содержат достаточной информации -- напиши:
               "В предоставленных документах нет достаточной информации для ответа на этот вопрос. Пожалуйста, уточните запрос."

            === ФОРМАТ ОТВЕТА ===
            Ты ОБЯЗАН вернуть ответ в следующем формате, используя указанные разделители:

            --- ОТВЕТ ---
            [Здесь твой подробный ответ на вопрос пользователя, основанный на документах]

            --- ИСТОЧНИКИ ---
            [Здесь список источников, из которых взята информация. Формат для каждого источника:
            - source: <полный путь к файлу или название>
              section: <название раздела>
              chunk_id: <идентификатор чанка>
            ]

            --- ЦИТАТЫ ---
            [Здесь фрагменты текста из найденных чанков, которые подтверждают ответ. Формат для каждой цитаты:
            - "[текст цитаты]" -- source: <название>, section: <раздел>, chunk_id: <идентификатор>
            ]

            === ВАЖНО ===
            - Ты ОБЯЗАН включить все три раздела: ОТВЕТ, ИСТОЧНИКИ, ЦИТАТЫ
            - В разделе ИСТОЧНИКИ укажи source + section + chunk_id для каждого чанка
            - В разделе ЦИТАТЫ приведи точные фрагменты текста из чанков
            - Если информации недостаточно, в разделе ОТВЕТ напиши "не знаю" и попроси уточнить

            === ДОКУМЕНТЫ ===
            {ragContext}
            === КОНЕЦ ДОКУМЕНТОВ ===

            === ВОПРОС ===
            {question}

            === ТВОЙ ОТВЕТ ===
            """;

        var ragSystemPrompt = "Ты -- аналитик, отвечающий на вопросы на основе документов. Ты обязан возвращать ответ в структурированном формате с источниками и цитатами.";
        var ragUserPrompt = $"=== ДОКУМЕНТЫ ===\n{ragContext}\n=== КОНЕЦ ДОКУМЕНТОВ ===\n\n=== ВОПРОС ===\n{question}\n\n=== ФОРМАТ ОТВЕТА ===\nТы ОБЯЗАН вернуть ответ в следующем формате, используя указанные разделители:\n\n--- ОТВЕТ ---\n[Подробный ответ на вопрос, основанный на документах]\n\n--- ИСТОЧНИКИ ---\n[Список источников в формате:\n- source: <путь/название>\n  section: <раздел>\n  chunk_id: <идентификатор чанка>]\n\n--- ЦИТАТЫ ---\n[Фрагменты текста из чанков в формате:\n- \"текст цитаты\" -- source: <название>, section: <раздел>, chunk_id: <идентификатор>]\n\nЕсли информации недостаточно, напиши в разделе ОТВЕТ: \"не знаю\" и попроси уточнить запрос.\n\n=== ТВОЙ ОТВЕТ ===";

        var answerWithRag = await CallLlmAsync(
            ctx.Config,
            ragSystemPrompt,
            ragUserPrompt,
            question,
            temperature: 0.1,
            maxTokens: 4096);

        // Парсим структурированный ответ RAG
        var (ragAnswer, ragSources, ragQuotes) = ParseRagResponse(answerWithRag);

        // Вывод результатов
        PrintRagComparison(question, searchResults, filteredResults, rejectedResults, answerWithoutRag, ragAnswer, ragSources, ragQuotes);

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
        Console.WriteLine($"  Сканирую {indexFiles.Count} индекс(ов)...");

        var allChunks = new List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)>();
        var keywords = ExtractKeywords(query);
        var phrases = ExtractPhrases(query);
        var totalChunks = 0;

        Console.WriteLine($"  Ключевые слова: {string.Join(", ", keywords)}");
        if (phrases.Count > 0)
            Console.WriteLine($"  Фразы: {string.Join(", ", phrases)}");

        foreach (var jsonFile in indexFiles)
        {
            var (storage, strategy, safeName) = ParseIndexFileName(jsonFile);
            var chunkCount = 0;

            try
            {
                var bytes = File.ReadAllBytes(jsonFile);
                bytes = StripUtf8Bom(bytes);
                using var doc = JsonDocument.Parse(bytes);
                var root = doc.RootElement;
                var chunksJson = root.GetProperty("chunks");
                chunkCount = chunksJson.GetArrayLength();
                totalChunks += chunkCount;

                for (var i = 0; i < chunkCount; i++)
                {
                    var chunkJson = chunksJson[i];
                    var text = chunkJson.GetProperty("text").GetString() ?? "";
                    var title = chunkJson.GetProperty("metadata").GetProperty("title").GetString() ?? "";
                    var source = chunkJson.GetProperty("metadata").GetProperty("source").GetString() ?? "";
                    var section = chunkJson.GetProperty("metadata").GetProperty("section").GetString() ?? "";

                    if (string.IsNullOrWhiteSpace(text)) continue;

                    var score = ComputeRelevanceScore(text, title, query, keywords, phrases);

                    if (score > 0)
                    {
                        var chunk = new DocumentChunk
                        {
                            Id = chunkJson.GetProperty("id").GetString() ?? "",
                            Text = text,
                            Metadata = new ChunkMetadata
                            {
                                Source = source,
                                Title = title,
                                Section = section,
                                ChunkId = chunkJson.GetProperty("metadata").GetProperty("chunk_id").GetString() ?? "",
                            },
                        };
                        allChunks.Add((chunk, 1.0f - score, strategy, safeName));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️  Ошибка чтения {jsonFile}: {ex.Message}");
            }
        }

        Console.WriteLine($"  Всего чанков в индексах: {totalChunks}, найдено релевантных: {allChunks.Count}");

        // Сортируем по score (по убыванию)
        allChunks.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var seenTexts = new HashSet<string>();
        var deduped = allChunks
            .Where(r => seenTexts.Add(r.Chunk.Text))
            .ToList();

        return deduped;
    }

    /// <summary>
    /// Этап 2: reranker / фильтр релевантности.
    /// Принимает все результаты поиска, фильтрует по порогу и берёт топ-K.
    /// Возвращает (отфильтрованные, отброшенные, статистику).
    /// </summary>
    private static (
        List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)> Kept,
        List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)> Rejected,
        int TotalBefore,
        int TotalAfter,
        float? MinKeptSimilarity,
        float? MaxRejectedSimilarity)
    RerankAndFilter(
        List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)> allResults,
        float minSimilarityThreshold,
        int topK)
    {
        var rejected = new List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)>();
        var kept = new List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)>();

        foreach (var result in allResults)
        {
            var similarity = 1.0f - result.Distance;
            if (similarity < minSimilarityThreshold)
            {
                rejected.Add(result);
            }
            else
            {
                kept.Add(result);
            }
        }

        // Берём топ-K из отфильтрованных (уже отсортированы по distance / asc)
        var finalResults = kept.Take(topK).ToList();

        // Если после фильтрации ничего не осталось — fallback на top-K из исходных
        if (finalResults.Count == 0 && allResults.Count > 0)
        {
            Console.WriteLine("   ⚠️  Все чанки отфильтрованы — используем top-K из исходных результатов");
            finalResults = allResults.Take(topK).ToList();
        }

        var minKeptSim = finalResults.Count > 0 ? (float?)(1.0f - finalResults.Max(r => r.Distance)) : null;
        var maxRejectSim = rejected.Count > 0 ? (float?)(1.0f - rejected.Min(r => r.Distance)) : null;

        return (finalResults, rejected, allResults.Count, finalResults.Count, minKeptSim, maxRejectSim);
    }

    private static float ComputeRelevanceScore(string text, string title, string query, List<string> keywords, List<string> phrases)
    {
        var textLower = text.ToLowerInvariant();
        var queryLower = query.ToLowerInvariant();
        var titleLower = title.ToLowerInvariant();

        float score = 0f;

        // 1. Точное совпадение всей фразы из вопроса (самый сильный сигнал)
        foreach (var phrase in phrases)
        {
            if (textLower.Contains(phrase.ToLowerInvariant()))
            {
                score += 0.4f;
            }
        }

        // 2. Точные совпадения ключевых слов
        int exactMatches = 0;
        foreach (var kw in keywords)
        {
            if (textLower.Contains(kw.ToLowerInvariant()))
            {
                exactMatches++;
                score += 0.08f;
            }
        }

        // 3. Морфологические вариации (суффиксы)
        int morphMatches = 0;
        foreach (var kw in keywords)
        {
            if (exactMatches > 0) continue; // Уже посчитали точное совпадение
            var found = false;
            foreach (var suffix in new[] { "ия", "ии", "ий", "ие", "ых", "ых", "ам", "ям", "ой", "ом", "ых", "ях", "ами", "ями", "ость", "ости", "остей" })
            {
                if (textLower.Contains(kw.ToLowerInvariant() + suffix) || textLower.Contains(kw.ToLowerInvariant().Substring(0, Math.Min(4, kw.Length)) + suffix))
                {
                    found = true;
                    break;
                }
            }
            if (found)
            {
                morphMatches++;
                score += 0.05f;
            }
        }

        // 4. Совпадение в заголовке чанка
        if (!string.IsNullOrEmpty(titleLower))
        {
            foreach (var kw in keywords)
            {
                if (titleLower.Contains(kw.ToLowerInvariant()))
                {
                    score += 0.15f;
                    break;
                }
            }
        }

        // 5. Штраф за слишком короткие чанки (менее 50 символов)
        if (text.Length < 50)
        {
            score *= 0.3f;
        }

        // 6. Бонус за плотность совпадений
        if (keywords.Count > 0 && exactMatches > 0)
        {
            var density = (float)exactMatches / keywords.Count;
            if (density > 0.5f)
            {
                score += 0.1f; // Бонус за высокую плотность
            }
        }

        // Нормализуем score в диапазон [0, 1]
        return Math.Min(1.0f, score);
    }

    private static List<string> ExtractPhrases(string text)
    {
        // Извлекаем фразы из вопроса — длинные последовательности слов (> 2 слов)
        var words = text.ToLowerInvariant()
            .Replace("?", " ")
            .Replace("!", " ")
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1)
            .ToList();

        var phrases = new List<string>();

        // Фразы из 2-4 слов
        for (var len = 2; len <= 4 && len <= words.Count; len++)
        {
            for (var i = 0; i <= words.Count - len; i++)
            {
                var phrase = string.Join(" ", words.Skip(i).Take(len));
                if (phrase.Split(' ').Length >= 2)
                {
                    phrases.Add(phrase);
                }
            }
        }

        return phrases.Distinct().ToList();
    }

    private static List<string> ExtractKeywords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "какие", "какая", "какой", "что", "как", "где", "почему", "когда",
            "в", "из", "с", "на", "и", "или", "не", "к", "у", "о", "при",
            "three", "what", "how", "where", "why", "when", "which", "are",
            "the", "a", "an", "is", "was", "were", "of", "for", "with", "on",
            "at", "by", "from", "as", "into", "through", "their", "been",
            "других", "другие", "также", "ещё", "более", "между",
        };

        return text.ToLowerInvariant()
            .Replace("?", " ")
            .Replace("!", " ")
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct()
            .ToList();
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

    private static byte[] StripUtf8Bom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return bytes[3..];
        return bytes;
    }

    private static string BuildRagContext(List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)> results)
    {
        var sb = new StringBuilder();

        // Сортируем по схожести (по убыванию) — лучшие чанки первыми
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

    /// <summary>
    /// Парсит структурированный ответ LLM на три раздела: ответ, источники, цитаты.
    /// </summary>
    private static (string Answer, List<string> Sources, List<string> Quotes) ParseRagResponse(string rawResponse)
    {
        var answer = rawResponse.Trim();
        var sources = new List<string>();
        var quotes = new List<string>();

        // Извлекаем раздел ОТВЕТ
        var answerIdx = rawResponse.IndexOf("--- ОТВЕТ ---", StringComparison.Ordinal);
        if (answerIdx >= 0)
        {
            var rest = rawResponse[(answerIdx + "--- ОТВЕТ ---".Length)..];
            var sourcesIdx = rest.IndexOf("--- ИСТОЧНИКИ ---", StringComparison.Ordinal);
            var quotesIdx = rest.IndexOf("--- ЦИТАТЫ ---", StringComparison.Ordinal);

            var answerEnd = int.MaxValue;
            if (sourcesIdx >= 0 && sourcesIdx < answerEnd) answerEnd = sourcesIdx;
            if (quotesIdx >= 0 && quotesIdx < answerEnd) answerEnd = quotesIdx;

            if (answerEnd < int.MaxValue)
                answer = rest[..answerEnd].Trim();
            else
                answer = rest.Trim();

            // Извлекаем раздел ИСТОЧНИКИ
            if (sourcesIdx >= 0)
            {
                var sourcesRest = rawResponse[(sourcesIdx + "--- ИСТОЧНИКИ ---".Length)..];
                var quotesIdxInRest = sourcesRest.IndexOf("--- ЦИТАТЫ ---", StringComparison.Ordinal);

                string sourcesText;
                if (quotesIdxInRest >= 0)
                    sourcesText = sourcesRest[..quotesIdxInRest].Trim();
                else
                    sourcesText = sourcesRest.Trim();

                foreach (var line in sourcesText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        sources.Add(trimmed);
                }
            }

            // Извлекаем раздел ЦИТАТЫ
            if (quotesIdx >= 0)
            {
                var quotesText = rawResponse[(quotesIdx + "--- ЦИТАТЫ ---".Length)..].Trim();

                foreach (var line in quotesText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        quotes.Add(trimmed);
                }
            }
        }

        return (answer, sources, quotes);
    }

    private static void PrintRagComparison(
        string question,
        List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)> allResults,
        List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)> filteredResults,
        List<(DocumentChunk Chunk, float Distance, string Strategy, string SafeName)> rejectedResults,
        string answerWithoutRag,
        string ragAnswer,
        List<string> ragSources,
        List<string> ragQuotes)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"📝 ВОПРОС: {question}");
        Console.WriteLine(new string('=', 80));
        Console.WriteLine();

        // Поиск — сравнение до и после фильтрации
        Console.WriteLine("🔎 РЕЗУЛЬТАТЫ ПОИСКА (до / после фильтрации):");
        Console.WriteLine(new string('-', 60));

        Console.WriteLine($"\n  📊 Всего найдено: {allResults.Count} чанков");
        Console.WriteLine($"  ✅ После фильтра: {filteredResults.Count} чанков");
        Console.WriteLine($"  ❌ Отброшено: {rejectedResults.Count} чанков");

        if (filteredResults.Count > 0)
        {
            var minSim = 1.0f - filteredResults.Max(r => r.Distance);
            var maxSim = 1.0f - filteredResults.Min(r => r.Distance);
            var avgSim = filteredResults.Average(r => 1.0f - r.Distance);
            Console.WriteLine($"  📈 Similarity: min={minSim:F3}, max={maxSim:F3}, avg={avgSim:F3}");
        }

        if (rejectedResults.Count > 0)
        {
            var maxRejectSim = 1.0f - rejectedResults.Min(r => r.Distance);
            var minRejectSim = 1.0f - rejectedResults.Max(r => r.Distance);
            var avgRejectSim = rejectedResults.Average(r => 1.0f - r.Distance);
            Console.WriteLine($"  📉 Отброшенные: min={minRejectSim:F3}, max={maxRejectSim:F3}, avg={avgRejectSim:F3}");
        }

        Console.WriteLine($"\n  📋 Отобранные чанки (топ-{filteredResults.Count}):");

        var grouped = filteredResults
            .GroupBy(r => r.SafeName)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var group in grouped)
        {
            Console.WriteLine($"\n    📁 {group.Key}");

            foreach (var (chunk, distance, strategy, _) in group)
            {
                var similarity = 1.0f - distance;
                var preview = chunk.Text.Length > 200 ? chunk.Text[..200] + "..." : chunk.Text;
                Console.WriteLine($"      [{strategy,-12}] {similarity:F3} | {preview}");
            }
        }

        if (rejectedResults.Count > 0)
        {
            Console.WriteLine($"\n  🗑️  Примеры отброшенных чанков (top-5 по similarity):");
            var topRejected = rejectedResults
                .OrderBy(r => r.Distance)
                .Take(5)
                .ToList();

            foreach (var (chunk, distance, strategy, safeName) in topRejected)
            {
                var similarity = 1.0f - distance;
                var preview = chunk.Text.Length > 150 ? chunk.Text[..150] + "..." : chunk.Text;
                Console.WriteLine($"      [{strategy,-12}] {similarity:F3} [{safeName}] {preview}");
            }
        }

        // ===== Ответ БЕЗ RAG =====
        Console.WriteLine();
        Console.WriteLine(new string('=', 80));
        Console.WriteLine("🤖 ОТВЕТ БЕЗ RAG (только вопрос → LLM):");
        Console.WriteLine(new string('-', 80));
        Console.WriteLine(answerWithoutRag);
        Console.WriteLine(new string('=', 80));

        // ===== Ответ С RAG (структурированный) =====
        Console.WriteLine();
        Console.WriteLine(new string('=', 80));
        Console.WriteLine("📚 ОТВЕТ С RAG (структурированный):");
        Console.WriteLine(new string('-', 80));

        Console.WriteLine("\n✅ Ответ:");
        Console.WriteLine(ragAnswer);

        if (ragSources.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("📑 Источники:");
            foreach (var source in ragSources)
            {
                Console.WriteLine($"  {source}");
            }
        }

        if (ragQuotes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("📖 Цитаты из документов:");
            foreach (var quote in ragQuotes)
            {
                Console.WriteLine($"  {quote}");
            }
        }

        Console.WriteLine(new string('=', 80));

        // Краткое сравнение
        Console.WriteLine();
        PrintComparisonSummary(answerWithoutRag, ragAnswer);
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
