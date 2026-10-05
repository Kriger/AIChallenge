using AIChallenge.Cli.Commands;
using AIChallenge.DocumentIndexing;
using AIChallenge.DocumentIndexing.Chunking;
using AIChallenge.DocumentIndexing.Embeddings;
using AIChallenge.DocumentIndexing.Models;
using AIChallenge.McpScheduler;
using AIChallenge.Models;
using System.Text;
using System.Text.Json;

namespace AIChallenge.Cli.Commands;

/// <summary>
/// Мини-чат с историей и RAG: каждый вопрос ищет контекст в документах,
/// отвечает с учётом найденной информации и всегда выводит источники.
/// </summary>
public class ChatCommand : CommandHandler
{
    public override string Name => "chat";

    private readonly List<ChatMessage> _history = new();
    private readonly TaskMemory _taskMemory = new();
    private readonly object _lock = new();

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  🗨️  RAG-ЧАТ — диалог с поиском по документами          ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("  Команды:");
        Console.WriteLine("    <текст>              — задать вопрос");
        Console.WriteLine("    /status              — показать состояние задачи");
        Console.WriteLine("    /goal <цель>         — установить цель диалога");
        Console.WriteLine("    /constraint <ограничение> — добавить ограничение");
        Console.WriteLine("    /term <имя> <определение> — зафиксировать термин");
        Console.WriteLine("    /clear               — очистить историю");
        Console.WriteLine("    /clear-task          — сбросить память задачи");
        Console.WriteLine("    /exit                — выйти из режима чата");
        Console.WriteLine();
        Console.WriteLine(new string('─', 60));
        Console.WriteLine();

        while (true)
        {
            Console.Write("  Вы: ");
            var input = Console.ReadLine()?.Trim();

            if (input is null) break;

            if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("выход", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                Console.WriteLine("  👋 Выход из режима чата.");
                Console.WriteLine();
                break;
            }

            // Память задачи
            if (input.Equals("/status", StringComparison.OrdinalIgnoreCase))
            {
                PrintTaskStatus();
                Console.WriteLine(new string('─', 60));
                Console.WriteLine();
                continue;
            }

            if (input.Equals("/clear-task", StringComparison.OrdinalIgnoreCase))
            {
                lock (_lock)
                {
                    _taskMemory.Clear();
                }
                Console.WriteLine();
                Console.WriteLine("  🗑️  Память задачи очищена.");
                Console.WriteLine(new string('─', 60));
                Console.WriteLine();
                continue;
            }

            if (input.StartsWith("/goal ", StringComparison.OrdinalIgnoreCase))
            {
                var goal = input["/goal ".Length..].Trim();
                if (string.IsNullOrEmpty(goal))
                {
                    Console.WriteLine("  ⚠️  Укажите цель: /goal <описание цели>");
                }
                else
                {
                    lock (_lock)
                    {
                        _taskMemory.Goal = goal;
                    }
                    Console.WriteLine($"  🎯 Цель зафиксирована: {goal}");
                }
                Console.WriteLine(new string('─', 60));
                Console.WriteLine();
                continue;
            }

            if (input.StartsWith("/constraint ", StringComparison.OrdinalIgnoreCase))
            {
                var constraint = input["/constraint ".Length..].Trim();
                if (string.IsNullOrEmpty(constraint))
                {
                    Console.WriteLine("  ⚠️  Укажите ограничение: /constraint <описание ограничения>");
                }
                else
                {
                    lock (_lock)
                    {
                        _taskMemory.Constraints.Add(constraint);
                    }
                    Console.WriteLine($"  🔒 Ограничение добавлено: {constraint}");
                }
                Console.WriteLine(new string('─', 60));
                Console.WriteLine();
                continue;
            }

            if (input.StartsWith("/term ", StringComparison.OrdinalIgnoreCase))
            {
                var args = input["/term ".Length..].Trim();
                var spaceIdx = args.IndexOf(' ');
                if (spaceIdx <= 0 || string.IsNullOrEmpty(args.Substring(spaceIdx + 1).Trim()))
                {
                    Console.WriteLine("  ⚠️  Укажите термин и определение: /term <имя> <определение>");
                }
                else
                {
                    var termName = args[..spaceIdx].Trim();
                    var termDef = args.Substring(spaceIdx + 1).Trim();
                    lock (_lock)
                    {
                        _taskMemory.Terms[termName] = termDef;
                    }
                    Console.WriteLine($"  📖 Термин зафиксирован: {termName} = {termDef}");
                }
                Console.WriteLine(new string('─', 60));
                Console.WriteLine();
                continue;
            }

            if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
            {
                lock (_lock)
                {
                    _history.Clear();
                }
                Console.WriteLine();
                Console.WriteLine("  🗑️  История очищена.");
                Console.WriteLine(new string('─', 60));
                Console.WriteLine();
                continue;
            }

            if (string.IsNullOrEmpty(input))
                continue;

            // Обработка вопроса
            await ProcessQuestionAsync(input, ctx);
        }

        return true;
    }

    private async Task ProcessQuestionAsync(string question, CommandContext ctx)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // 1. Поиск контекста через RAG
        Console.WriteLine("  🔍 Поиск по документам...");
        var searchResults = await SearchDocumentsAsync(question, ctx);

        if (searchResults.Count == 0)
        {
            Console.WriteLine("  ⚠️  По документам ничего не найдено. Отвечаю без RAG-контекста.");
        }
        else
        {
            Console.WriteLine($"  ✅ Найдено {searchResults.Count} релевантных чанков.");
        }

        // 2. Формирование контекста из найденных чанков
        var ragContext = BuildRagContext(searchResults);

        // 3. Построение истории диалога (последние N сообщений)
        var dialogueHistory = BuildDialogueHistory(question);

        // 3.5. Получение текущей памяти задачи
        var taskMemorySnapshot = GetTaskMemorySnapshot();

        // 4. Запрос к LLM с учётом RAG-контекста, истории и памяти задачи
        Console.WriteLine("  💭 Формирую ответ...");
        var answer = await CallLlmWithRagAsync(
            ctx.Config,
            question,
            ragContext,
            dialogueHistory,
            taskMemorySnapshot,
            searchResults);

        stopwatch.Stop();

        // 5. Сохранение в историю
        lock (_lock)
        {
            _history.Add(new ChatMessage { Role = "user", Content = question });
            _history.Add(new ChatMessage { Role = "assistant", Content = answer, Sources = searchResults });
        }

        // 6. Вывод ответа
        Console.WriteLine();
        Console.WriteLine("  ╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("  ║  🤖 Ответ:");
        Console.WriteLine("  ╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  {answer}");
        Console.WriteLine();

        // 7. Всегда выводим источники
        PrintSources(searchResults);

        Console.WriteLine($"  ⏱  Время ответа: {stopwatch.ElapsedMilliseconds} мс");
        Console.WriteLine();
        Console.WriteLine(new string('─', 60));
        Console.WriteLine();
    }

    private async Task<List<(DocumentChunk Chunk, float Distance, string Strategy)>> SearchDocumentsAsync(
        string query, CommandContext ctx)
    {
        var indexDir = GetIndexDir(ctx);
        var indexFiles = GetIndexFiles(indexDir);

        if (indexFiles.Count == 0)
            return new List<(DocumentChunk, float, string)>();

        var allResults = new List<(DocumentChunk Chunk, float Distance, string Strategy)>();

        foreach (var jsonFile in indexFiles)
        {
            var binFile = $"{jsonFile.Substring(0, jsonFile.Length - 5)}.bin";
            var (storage, strategy) = ParseIndexFileName(jsonFile);

            try
            {
                // Генерируем эмбеддинг запроса
                var embeddingProvider = new OllamaEmbeddingProvider(
                    ctx.DocIndexConfig.OllamaUrl,
                    ctx.DocIndexConfig.OllamaEmbeddingModel);

                var queryEmbedding = await embeddingProvider.GenerateEmbeddingAsync(query);

                // Загружаем чанки из JSON
                var jsonBytes = await File.ReadAllBytesAsync(jsonFile);
                jsonBytes = StripUtf8Bom(jsonBytes);
                using var doc = JsonDocument.Parse(jsonBytes);
                var root = doc.RootElement;
                var chunksJson = root.GetProperty("chunks");
                var chunkCount = chunksJson.GetArrayLength();

                // Считываем эмбеддинги и вычисляем расстояние
                var results = new List<(DocumentChunk Chunk, float Distance)>();
                var idx = 0;

                await foreach (var embedding in embeddingProvider.LoadEmbeddingsAsync(binFile))
                {
                    var distance = CosineDistance(queryEmbedding, embedding);
                    var chunkJson = chunksJson[idx];
                    var chunk = CreateChunkFromJson(chunkJson);
                    results.Add((chunk, distance));
                    idx++;
                }

                // Сортируем и берём топ-K
                results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                var topK = ctx.DocIndexConfig.RagReRankTopK;

                foreach (var (chunk, distance) in results.Take(topK))
                {
                    allResults.Add((chunk, distance, strategy));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️  Ошибка поиска по {Path.GetFileName(jsonFile)}: {ex.Message}");
            }
        }

        // Глобальная сортировка и дедупликация
        allResults.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var seenTexts = new HashSet<string>();
        var deduped = allResults
            .Where(r => seenTexts.Add(r.Chunk.Text))
            .ToList();

        return deduped;
    }

    private static string BuildRagContext(List<(DocumentChunk Chunk, float Distance, string Strategy)> results)
    {
        var sb = new StringBuilder();

        // Группируем чанки по файлу
        var grouped = results
            .OrderByDescending(r => 1.0f - r.Distance)
            .GroupBy(r => r.Chunk.Metadata.Source)
            .ToList();

        foreach (var group in grouped)
        {
            var fileName = Path.GetFileName(group.Key);
            sb.AppendLine($"--- Файл: {fileName} ---");

            foreach (var (chunk, distance, strategy) in group)
            {
                var similarity = 1.0f - distance;
                sb.AppendLine($"  [Раздел: {chunk.Metadata.Section}] [Чанк: {chunk.Metadata.ChunkId}] [Стратегия: {strategy}] [Схожесть: {similarity:F3}]");
                sb.AppendLine(chunk.Text);
                sb.AppendLine();
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string BuildDialogueHistory(string currentQuestion)
    {
        lock (_lock)
        {
            // Берём последние 10 сообщений для контекста
            var recent = _history.Skip(Math.Max(0, _history.Count - 10)).ToList();

            if (recent.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("История диалога (для контекста):");

            foreach (var msg in recent)
            {
                sb.Append(msg.Role == "user" ? "  👤 Вы: " : "  🤖 Ассистент: ");
                sb.AppendLine(msg.Content);
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    private async Task<string> CallLlmWithRagAsync(
        GigaChatConfig config,
        string question,
        string ragContext,
        string dialogueHistory,
        string taskMemory,
        List<(DocumentChunk Chunk, float Distance, string Strategy)> searchResults)
    {
        var client = new GigaChatClient(config.ClientId, config.ClientSecret);

        var systemPrompt = $"""
            Ты — полезный ассистент, отвечающий на вопросы на основе предоставленных документов.

            ИНСТРУКЦИИ:
            1. Используй информацию из документов (RAG-контекст) для ответа.
            2. Если ответ есть в документах — приведи его подробно.
            3. Если ответ есть частично — укажи, что именно найдено.
            4. Если документы НЕ содержат достаточной информации — честно скажи об этом.
            5. Ссылайся на источники, когда это уместно.
            6. Учитывай историю диалога для контекста.
            7. Помни о целях, ограничениях и терминах из памяти задачи.

            {taskMemory}
            """;

        var userPrompt = $"""
            {dialogueHistory}
            === ДОКУМЕНТЫ (RAG-контекст) ===
            {ragContext}
            === КОНЕЦ ДОКУМЕНТОВ ===

            === ВОПРОС ===
            {question}

            === ТВОЙ ОТВЕТ ===
            """;

        var answer = await client.ChatAsync(
            model: config.Model,
            systemPrompt: systemPrompt,
            userPrompt: userPrompt,
            temperature: 0.3,
            maxTokens: 4096);

        return answer;
    }

    private static void PrintSources(List<(DocumentChunk Chunk, float Distance, string Strategy)> results)
    {
        if (results.Count == 0)
        {
            Console.WriteLine("  📑 Источники: не найдено (ответ без RAG-контекста)");
            return;
        }

        const float minSimilarity = 0.65f;

        // Фильтруем чанки с схожестью ниже порога
        var filtered = results
            .Where(r => (1.0f - r.Distance) >= minSimilarity)
            .ToList();

        if (filtered.Count == 0)
        {
            Console.WriteLine($"  📑 Источники: найдено {results.Count} чанков, но ни один не превысил порог схожести ({minSimilarity:F1}%)");
            return;
        }

        Console.WriteLine("  📑 Источники:");
        Console.WriteLine("  " + new string('─', 56));

        // Группируем чанки по файлу
        var grouped = filtered
            .GroupBy(r => r.Chunk.Metadata.Source)
            .ToList();

        foreach (var group in grouped)
        {
            var fileName = Path.GetFileName(group.Key);
            Console.WriteLine();
            Console.WriteLine($"  📄 {fileName}");
            Console.WriteLine("  " + new string('─', 50));

            foreach (var (chunk, distance, strategy) in group)
            {
                var similarity = 1.0f - distance;
                Console.WriteLine($"    Раздел: {chunk.Metadata.Section}");
                Console.WriteLine($"    Чанк: {chunk.Metadata.ChunkId}");
                Console.WriteLine($"    Стратегия: {strategy} | Схожесть: {similarity:F3}");
                Console.WriteLine();
            }
        }
    }

    private string GetIndexDir(CommandContext ctx)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, ctx.DocIndexConfig.IndexSubDir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private List<string> GetIndexFiles(string indexDir)
        => Directory.GetFiles(indexDir, "index_faiss_*")
            .Where(f => f.EndsWith(".json"))
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();

    private static (string storage, string strategy) ParseIndexFileName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        // index_faiss_fixed_size_... или index_faiss_structural_...
        var rest = baseName.Substring("index_".Length);
        var firstIdx = rest.IndexOf('_');
        if (firstIdx <= 0) return (rest, "");

        var storage = rest.Substring(0, firstIdx);
        var rest2 = rest.Substring(firstIdx + 1);

        var knownStrategies = new[] { "fixed_size", "structural" };
        foreach (var known in knownStrategies)
        {
            var prefix = known + "_";
            if (rest2.StartsWith(prefix, StringComparison.Ordinal))
            {
                var strategy = rest2.Substring(prefix.Length);
                return (storage, strategy);
            }
        }

        return (storage, rest2);
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

    private static byte[] StripUtf8Bom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return bytes[3..];
        return bytes;
    }

    /// <summary>
    /// Сообщение в истории чата.
    /// </summary>
    private sealed class ChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public List<(DocumentChunk Chunk, float Distance, string Strategy)>? Sources { get; set; }
    }

    /// <summary>
    /// Память задачи: цель, ограничения, термины, уточнения.
    /// </summary>
    private sealed class TaskMemory
    {
        public string Goal { get; set; } = "";
        public List<string> Constraints { get; } = new();
        public Dictionary<string, string> Terms { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Clarifications { get; } = new();

        public void Clear()
        {
            Goal = "";
            Constraints.Clear();
            Terms.Clear();
            Clarifications.Clear();
        }
    }

    private void PrintTaskStatus()
    {
        lock (_lock)
        {
            var hasData = !string.IsNullOrEmpty(_taskMemory.Goal) ||
                          _taskMemory.Constraints.Count > 0 ||
                          _taskMemory.Terms.Count > 0 ||
                          _taskMemory.Clarifications.Count > 0;

            if (!hasData)
            {
                Console.WriteLine("  📋 Память задачи пуста.");
                Console.WriteLine("     Установите цель: /goal <описание>");
                Console.WriteLine("     Добавьте ограничение: /constraint <текст>");
                Console.WriteLine("     Зафиксируйте термин: /term <имя> <определение>");
                return;
            }

            Console.WriteLine("  📋 Состояние задачи:");
            Console.WriteLine("  " + new string('─', 50));

            // Цель
            if (!string.IsNullOrEmpty(_taskMemory.Goal))
            {
                Console.WriteLine();
                Console.WriteLine($"  🎯 Цель:");
                Console.WriteLine($"     {_taskMemory.Goal}");
            }

            // Ограничения
            if (_taskMemory.Constraints.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"  🔒 Ограничения ({_taskMemory.Constraints.Count}):");
                foreach (var c in _taskMemory.Constraints)
                {
                    Console.WriteLine($"     • {c}");
                }
            }

            // Термины
            if (_taskMemory.Terms.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"  📖 Термины ({_taskMemory.Terms.Count}):");
                foreach (var (name, def) in _taskMemory.Terms)
                {
                    Console.WriteLine($"     • {name}: {def}");
                }
            }

            // Уточнения
            if (_taskMemory.Clarifications.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"  💬 Уточнения ({_taskMemory.Clarifications.Count}):");
                foreach (var cl in _taskMemory.Clarifications)
                {
                    Console.WriteLine($"     • {cl}");
                }
            }
        }
    }

    private string GetTaskMemorySnapshot()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(_taskMemory.Goal))
            {
                sb.AppendLine($"ЦЕЛЬ ДИАЛОГА: {_taskMemory.Goal}");
            }

            if (_taskMemory.Constraints.Count > 0)
            {
                sb.AppendLine("ОГРАНИЧЕНИЯ:");
                foreach (var c in _taskMemory.Constraints)
                {
                    sb.AppendLine($"  - {c}");
                }
            }

            if (_taskMemory.Terms.Count > 0)
            {
                sb.AppendLine("ТЕРМИНЫ:");
                foreach (var (name, def) in _taskMemory.Terms)
                {
                    sb.AppendLine($"  - {name}: {def}");
                }
            }

            if (_taskMemory.Clarifications.Count > 0)
            {
                sb.AppendLine("УТОЧНЕНИЯ:");
                foreach (var cl in _taskMemory.Clarifications)
                {
                    sb.AppendLine($"  - {cl}");
                }
            }

            if (sb.Length == 0)
                return "";

            return $"ПАМЯТЬ ЗАДАЧИ:\n{sb}\nУЧИТЫВАЙ ЭТУ ИНФОРМАЦИЮ ПРИ ФОРМИРОВАНИИ ОТВЕТА.";
        }
    }
}
