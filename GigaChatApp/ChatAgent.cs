using GigaChatApp.Infrastructure;
using GigaChatApp.Models;
using GigaChatApp.Services;
using System.Text;

namespace GigaChatApp;

/// <summary>
/// Агент для общения с LLM через GigaChat API.
///
/// Настоящий агент — самостоятельная сущность с собственной логикой:
/// - Кэширует ответы на похожие запросы (fuzzy-поиск)
/// - Принимает решение о retry на основе типа ошибки
/// - Собирает метрики (количество, время, токены, success rate)
/// - Логирует все ключевые события
/// - Хранит историю диалога
/// - Долгосрочная память: сохраняет и извлекает факты из диалога
/// </summary>
public class ChatAgent
{
    private readonly ChatClient _httpClient;
    private readonly AuthClient _authClient;
    private readonly GigaChatConfig _config;

    private readonly List<ApiMessage> _history = new();

    /// <summary>Собственная история диалога.</summary>
    public IReadOnlyList<ApiMessage> History => _history;

    /// <summary>Менеджер памяти (краткосрочная, рабочая, долгосрочная).</summary>
    public MemoryManager MemoryManager { get; }

    /// <summary>Долгосрочная память (для обратной совместимости).</summary>
    public LongTermMemory Memory => MemoryManager.LongTerm;

    /// <summary>Текущая модель.</summary>
    public string Model
    {
        get => _model;
        set
        {
            _model = value;
            _planner?.UpdateModel(value);
        }
    }
    private string _model = "GigaChat-2";
    private Planner? _planner;

    /// <summary>Системное сообщение.</summary>
    public string SystemMessage { get; set; } = string.Empty;

    /// <summary>
    /// Профиль агента — персонализация поведения.
    /// </summary>
    public AgentProfile AgentProfile { get; set; } = null!;

    /// <summary>Максимальное количество токенов.</summary>
    public int MaxTokens { get; set; } = 0;

    /// <summary>Температура генерации.</summary>
    public double? Temperature { get; set; }

    /// <summary>Стоп-последовательности.</summary>
    public string[] StopSequences { get; set; } = Array.Empty<string>();

    /// <summary>Кэш ответов.</summary>
    public RequestCache Cache { get; }

    /// <summary>Логгер.</summary>
    public AgentLogger Logger { get; }

    /// <summary>Метрики.</summary>
    public AgentMetrics Metrics { get; } = new();

    /// <summary>Адаптивное поведение.</summary>
    public AdaptiveBehavior Adaptive { get; set; } = null!;

    /// <summary>Планировщик.</summary>
    public Planner Planner { get; set; } = null!;

    /// <summary>Управление контекстом.</summary>
    public ContextManager ContextManager { get; }

    /// <summary>Конфигурация GigaChat.</summary>
    public GigaChatConfig Config { get; set; } = null!;

    public ChatAgent(ChatClient httpClient, AuthClient authClient,
        RequestCache? cache = null, AgentLogger? logger = null, MemoryManager? memoryManager = null,
        AdaptiveBehavior? adaptive = null, Planner? planner = null, ContextManager? contextManager = null)
    {
        _httpClient = httpClient;
        _authClient = authClient;
        Cache = cache ?? new RequestCache();
        Logger = logger ?? new AgentLogger();
        MemoryManager = memoryManager ?? new MemoryManager(Logger);
        Adaptive = adaptive ?? new AdaptiveBehavior(Metrics, Cache, Logger);
        _planner = planner ?? new Planner(httpClient, authClient, Model, Logger, MemoryManager);
        Planner = _planner;
        ContextManager = contextManager ?? new ContextManager(httpClient, authClient, Logger);
        _config = new GigaChatConfig();
    }

    /// <summary>
    /// Обрабатывает запрос пользователя:
    /// 1. Проверяет кэш (fuzzy-поиск)
    /// 2. Ищет релевантные факты в памяти
    /// 3. Если запрос сложный — декомпозирует на подзадачи (Planning)
    /// 4. Если простой — отправляет в API с retry-логикой
    /// 5. Извлекает новые факты из диалога
    /// 6. Сохраняет ответ в кэш и историю
    /// 7. Собирает метрики
    /// 8. Адаптирует параметры на основе метрик
    /// </summary>
    public async Task<AgentResult> ProcessRequestAsync(string userMessage)
    {
        Metrics.TotalRequests++;
        Logger.Info($"Запрос: \"{userMessage[..Math.Min(userMessage.Length, 80)]}\"");

        // 1. Проверяем кэш
        var cachedAnswer = Cache.TryGet(userMessage);
        if (cachedAnswer is not null)
        {
            Metrics.CachedRequests++;
            Logger.Info($"Кэш: найден ответ для похожего запроса");

            return new AgentResult
            {
                Answer = cachedAnswer,
                Source = Source.Cache,
            };
        }

        // 2. Ищем релевантные факты из LongTermMemory
        // Факты ищутся ВСЕГДА, независимо от стратегии контекста.
        // При Branching/StickyFacts дополнительно используются факты ветки (через systemMessages).
        List<Fact> relevantFacts = new();
        relevantFacts = MemoryManager.LongTerm.FindRelevant(userMessage);
        if (relevantFacts.Count > 0)
        {
            Logger.Info($"Память: найдено {relevantFacts.Count} релевантных факт(ов)");
            foreach (var fact in relevantFacts)
            {
                Logger.Debug($"  → {fact.Key}: {Truncate(fact.Value, 80)}");
            }
        }

        // 3. Проверяем, нужен ли Planning
        if (Config.PlannerEnabled)
        {
            var needs = Planner.NeedsDecomposition(userMessage);
            if (!needs)
            {
                var tooShort = userMessage.Length < Planner.MinComplexityLength;
                var noKeyword = !Planner.ComplexityKeywords.Any(kw => userMessage.ToLowerInvariant().Contains(kw));
                Logger.Debug($"Planning пропущен: длина={userMessage.Length} (нужно ≥{Planner.MinComplexityLength}), ключевое слово: {(noKeyword ? "нет" : "да")}");
            }
            if (needs)
            {
                // Пре-чек инвариантов ДО вызова LLM
                var invariants = AgentProfile?.Invariants;
                if (invariants is { Count: > 0 })
                {
                    var violation = Planner.CheckInvariantViolation(userMessage, invariants);
                    if (violation is not null)
                    {
                        var refusalAnswer = $"""
                            ⛔ Я не могу выполнить этот запрос, потому что он нарушает инвариант:

                              ⛔ {violation}

                            Инварианты — непреложные правила проекта. Давай найдём альтернативу,
                            которая удовлетворяет твою потребность, но остаётся в рамках принятых решений.
                            """;

                        Logger.Info($"Прервано на пре-чеке: {violation}");
                        return new AgentResult
                        {
                            Answer = refusalAnswer,
                            Source = Source.Api,
                        };
                    }
                }

                Logger.Info($"Запрос сложный, запускаю Planning...");
                var planResult = await ExecuteWithPlanningAsync(userMessage, relevantFacts, invariants);
                return planResult;
            }
        }

        // 4. Обычный запрос — отправляем в API с контекстом
        var result = await SendWithRetryAsync(userMessage, relevantFacts);

        // 5. Извлекаем новые факты из диалога
        if (result.IsSuccess)
        {
            Logger.Info("Запущено извлечение фактов из диалога...");
            // Передаём полный диалог, а не только последние 2 сообщения
            var dialogue = MemoryManager.ShortTerm.GetAll();
            ExtractFactsFromDialogue(userMessage, result.Answer, dialogue);
        }

        // 6. Сохраняем в кэш и историю
        if (result.IsSuccess)
        {
            Cache.Add(userMessage, result.Answer);
            _history.Add(new ApiMessage { Role = "assistant", Content = result.Answer });
            Metrics.SuccessfulRequests++;
            Metrics.TotalDuration += result.Duration;
            if (result.Usage is not null)
            {
                Metrics.TotalPromptTokens += result.Usage.PromptTokens;
                Metrics.TotalCompletionTokens += result.Usage.CompletionTokens;
            }
            Logger.Info($"Ответ получен ({result.Duration.TotalMilliseconds:F0} мс)");
        }
        else
        {
            Metrics.FailedRequests++;
            Logger.Error($"Ошибка: {result.Error}");
        }

        // 8. Адаптация на основе метрик
        Adaptive.Adapt();

        // 9. Выводим сохранённые факты
        PrintSavedFacts();

        return result;
    }

    /// <summary>
    /// Выполняет запрос через Planning — декомпозирует на подзадачи, выполняет, комбинирует.
    /// </summary>
    private async Task<AgentResult> ExecuteWithPlanningAsync(string userMessage, List<Fact> relevantFacts, List<string>? invariants)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("📋 Создаю план выполнения...");
        Console.ResetColor();

        // Создаём план
        var plan = await Planner.CreatePlanAsync(userMessage, relevantFacts, invariants);
        if (plan is null)
        {
            Logger.Info("План не создан, обрабатываю как обычный запрос");
            return await SendWithRetryAsync(userMessage, relevantFacts);
        }

        // Обработка отказа из-за нарушения инвариантов
        if (!string.IsNullOrEmpty(plan.RefusalReason))
        {
            var refusalAnswer = $"""
                ⛔ Я не могу выполнить этот запрос, потому что он нарушает инвариант:

                  {plan.RefusalReason}

                Инварианты — непреложные правила проекта. Давай найдём альтернативу,
                которая удовлетворяет твою потребность, но остаётся в рамках принятых решений.
                """;

            Logger.Info($"Планировщик отказал: {plan.RefusalReason}");
            return new AgentResult
            {
                Answer = refusalAnswer,
                Source = Source.Api,
            };
        }

        if (plan.Tasks.Count == 0)
        {
            Logger.Info("План не создан, обрабатываю как обычный запрос");
            return await SendWithRetryAsync(userMessage, relevantFacts);
        }

        Logger.Info($"Создан план с {plan.Tasks.Count} подзадачами");

        // Сохраняем план и подзадачи в рабочую память
        MemoryManager.Working.SavePlan(plan);
        MemoryManager.Working.Save("request", userMessage, "request");
        Logger.Info($"План сохранён в рабочую память: {plan.Tasks.Count} подзадач");

        // Показываем план пользователю
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("📋 План выполнения:");
        Console.ResetColor();
        foreach (var task in plan.Tasks)
        {
            Console.WriteLine($"   {task.Id}. {task.Description}");
        }
        Console.WriteLine();

        // Выполняем план
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("⏳ Выполняю подзадачи...");
        Console.ResetColor();

        plan = await Planner.ExecutePlanAsync(plan, relevantFacts);

        // Сохраняем результаты подзадач в рабочую память
        for (int i = 0; i < plan.Tasks.Count; i++)
        {
            var task = plan.Tasks[i];
            MemoryManager.Working.Save($"task.{i}.result", task.Result, "task_result");
            MemoryManager.Working.Save($"task.{i}.status", task.StatusText, "task_status");
        }
        if (plan.FinalAnswer is not null)
        {
            MemoryManager.Working.Save("final_answer", plan.FinalAnswer, "plan_result");
        }

        // Показываем статус подзадач
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("📊 Статус подзадач:");
        Console.ResetColor();
        foreach (var task in plan.Tasks)
        {
            var statusIcon = task.Status switch
            {
                Models.TaskStatus.Completed => "✅",
                Models.TaskStatus.Failed => "❌",
                Models.TaskStatus.Running => "🔄",
                _ => "⏳",
            };
            Console.WriteLine($"   {statusIcon} [{task.StatusText,12}] {task.Description}");
            if (task.Duration.HasValue)
            {
                Console.Write($"   ⏱ {task.Duration.Value.TotalMilliseconds:F0} мс");
            }
            Console.WriteLine();
        }
        Console.WriteLine();
        Console.WriteLine($"   Общая длительность: {plan.TotalDuration.TotalMilliseconds:F0} мс");
        Console.WriteLine();

        // Выводим сохранённые факты
        PrintSavedFacts();

        // Возвращаем финальный ответ
        return new AgentResult
        {
            Answer = plan.FinalAnswer ?? "Нет ответа",
            Duration = plan.TotalDuration,
            Source = Source.Api
        };
    }

    /// <summary>
    /// Отправляет запрос в API с интеллектуальным retry.
    /// Retry только для transient-ошибок и 5xx. 4xx — сразу возвращаем ошибку.
    /// </summary>
    /// <param name="userMessage">Сообщение пользователя.</param>
    /// <param name="relevantFacts">Релевантные факты из памяти для контекста.</param>
    private async Task<AgentResult> SendWithRetryAsync(string userMessage, List<Fact> relevantFacts)
    {
        // Добавляем запрос в полную историю
        _history.Add(new ApiMessage { Role = "user", Content = userMessage });

        // Добавляем в краткосрочную память (диалог)
        MemoryManager.ShortTerm.Add("user", userMessage);

        // Добавляем в ContextManager
        ContextManager.AddMessage(new ApiMessage { Role = "user", Content = userMessage });

        // Формируем контекст для отправки
        var contextResult = ContextManager.BuildContext();

        // Обновляем метрики
        Metrics.LastContextTokens = contextResult.CompressedTokens;
        Metrics.TotalContextTokens += contextResult.CompressedTokens;
        Metrics.ContextCompressionEnabled = ContextManager.Config.Enabled;

        // Формируем расширенное системное сообщение с фактами и summary
        var extendedSystemMessage = BuildExtendedSystemMessage(relevantFacts, contextResult);

        // Логирование для отладки — что уходит в API
        Logger.Debug($"[API] Системное сообщение: {extendedSystemMessage[..Math.Min(200, extendedSystemMessage.Length)]}...");
        Logger.Debug($"[API] Фактов LongTerm: {relevantFacts.Count}");
        Logger.Debug($"[API] Recent messages: {contextResult.RecentMessages.Count}, system messages: {contextResult.SystemMessages.Count}");

        // Детальное логирование фактов
        if (relevantFacts.Count > 0)
        {
            Logger.Debug($"[API] Факты в системном сообщении:");
            foreach (var fact in relevantFacts.Take(5))
            {
                Logger.Debug($"  → [{fact.Key}] {Truncate(fact.Value, 100)}");
            }
        }

        if (contextResult.SystemMessages.Count > 0)
        {
            Logger.Debug($"[API] SystemMessages от стратегии:");
            foreach (var sm in contextResult.SystemMessages)
            {
                Logger.Debug($"  → [{sm.Role}] {sm.Content[..Math.Min(100, sm.Content.Length)]}...");
            }
        }

        var lastException = default(Exception);
        var retryCount = 0;

        for (var attempt = 0; attempt <= Metrics.RetryCount; attempt++)
        {
            try
            {
                var token = await _authClient.GetAccessTokenAsync();

                var apiResponse = await _httpClient.SendCompletionAsync(
                    Model,
                    contextResult.RecentMessages,
                    MaxTokens > 0 ? MaxTokens : (int?)null,
                    Temperature,
                    StopSequences,
                    extendedSystemMessage,
                    token,
                    contextResult.SystemMessages
                );

                // Добавляем ответ в историю, краткосрочную память и ContextManager
                var assistantMessage = new ApiMessage { Role = "assistant", Content = apiResponse.Content };
                _history.Add(assistantMessage);
                MemoryManager.ShortTerm.Add("assistant", apiResponse.Content);
                ContextManager.AddMessage(assistantMessage);

                return new AgentResult
                {
                    Answer = apiResponse.Content,
                    Duration = apiResponse.Duration,
                    Usage = apiResponse.Usage,
                    Source = Source.Api,
                    CurrentRequestTokens = contextResult.CompressedTokens,
                    HistoryTokens = contextResult.OriginalTokens,
                };
            }
            catch (Exception ex)
            {
                lastException = ex;
                var category = ClassifyError(ex);

                Logger.Debug($"Попытка {attempt + 1}: {ex.GetType().Name} — {category}");

                // Решаем, стоит ли retry
                if (!ShouldRetry(category, attempt))
                {
                    Logger.Warning($"Retry не применим: {category}");
                    break;
                }

                retryCount++;
                Metrics.RetryAttempts++;
                Logger.Info($"Повторная попытка #{retryCount} через {Metrics.RetryDelayMs} мс");
                await Task.Delay(Metrics.RetryDelayMs);
            }
        }

        return new AgentResult
        {
            Error = lastException!.Message,
            Source = Source.Api,
        };
    }

    /// <summary>
    /// Классифицирует ошибку для принятия решения о retry.
    /// </summary>
    private static ErrorCategory ClassifyError(Exception ex)
    {
        if (ex is HttpRequestException)
            return ErrorCategory.Transient;

        if (ex is TaskCanceledException or TimeoutException)
            return ErrorCategory.Transient;

        // По содержимому сообщения ищем HTTP-код
        var msg = ex.Message.ToLowerInvariant();
        if (msg.Contains("4") && (msg.Contains("status") || msg.Contains("http")))
        {
            // 4xx — клиентская ошибка, retry не поможет
            return ErrorCategory.ClientError;
        }

        if (msg.Contains("5") && (msg.Contains("status") || msg.Contains("http")))
        {
            // 5xx — серверная ошибка, стоит retry
            return ErrorCategory.ServerError;
        }

        return ErrorCategory.Unknown;
    }

    /// <summary>
    /// Определяет, стоит ли повторять попытку.
    /// </summary>
    private static bool ShouldRetry(ErrorCategory category, int currentAttempt)
    {
        // 4xx — повторять бессмысленно
        if (category == ErrorCategory.ClientError)
            return false;

        // Неизвестная ошибка — не retry
        if (category == ErrorCategory.Unknown)
            return false;

        // Transient и ServerError — retry
        return true;
    }

    /// <summary>
    /// Формирует расширенное системное сообщение с релевантными фактами и summary.
    /// Порядок: Профиль агента → LongTerm факты → summary.
    /// Профиль — самый первый, чтобы LLLM сразу понял контекст.
    /// </summary>
    private string BuildExtendedSystemMessage(List<Fact> relevantFacts, ContextResult? contextResult = null)
    {
        var sb = new StringBuilder();

        // === ИНВАРИАНТЫ — САМЫЙ ПЕРВЫЙ БЛОК, МАКСИМАЛЬНЫЙ ПРИОРИТЕТ ===
        if (AgentProfile is not null && AgentProfile.Invariants.Count > 0)
        {
            sb.AppendLine("⛔⛔⛔ НЕПРЕЛОЖНЫЕ ИНВАРИАНТЫ (СТРОГОЕ ПРАВИЛО, НЕ НАРУШАТЬ) ⛔⛔⛔");
            sb.AppendLine();
            sb.AppendLine("Эти правила — абсолютный приоритет. Они имеют ВЫСШУЮ важность по сравнению со всеми остальными инструкциями.");
            sb.AppendLine("Если запрос пользователя противоречит любому из этих правил — ТЫ ОБЯЗАН ОТКАЗАТЬ.");
            sb.AppendLine("Не предлагай обходных путей, не игнорируй эти правила, не адаптируйся под пользователя.");
            sb.AppendLine();
            sb.AppendLine("Формат ответа при конфликте:");
            sb.AppendLine("1. Чётко скажи: «Я не могу предложить это, потому что...»");
            sb.AppendLine("2. Укажи конкретный нарушенный инвариант");
            sb.AppendLine("3. Предложи альтернативу, которая удовлетворяет потребность пользователя, но НЕ нарушает инвариант");
            sb.AppendLine();
            sb.AppendLine("Инварианты:");
            foreach (var inv in AgentProfile.Invariants)
            {
                sb.AppendLine($"  ⛔ {inv}");
            }
            sb.AppendLine();
            sb.AppendLine("=== КОНЕЦ ИНВАРИАНТОВ ===");
            sb.AppendLine();
        }

        // 1. Профиль агента
        if (AgentProfile is not null)
        {
            var profilePrompt = AgentProfile.BuildSystemPrompt();
            sb.AppendLine(profilePrompt);
            sb.AppendLine();
        }

        // 2. Факты из долгосрочной памяти — ПОСЛЕ профиля
        if (relevantFacts.Count > 0)
        {
            var topFacts = relevantFacts.Take(10).ToList();

            sb.AppendLine("=== КОНТЕКСТ ИЗ ПАМЯТИ ===");
            sb.AppendLine("Ниже — ключевая информация из предыдущих обсуждений.");

            foreach (var fact in topFacts)
            {
                sb.AppendLine($"• [{fact.Key}] {fact.Value}");
            }

            sb.AppendLine();
            sb.AppendLine("=== КОНЕЦ КОНТЕКСТА ===");
            sb.AppendLine();
        }

        // 3. Оригинальное системное сообщение (роль агента)
        if (!string.IsNullOrEmpty(SystemMessage))
        {
            sb.AppendLine(SystemMessage);
        }

        // 4. Summary из ContextManager (сжатая история диалога)
        if (contextResult is { IsCompressed: true, SummaryText: not "" })
        {
            sb.AppendLine();
            sb.AppendLine("=== СЖАТАЯ ИСТОРИЯ ДИАЛОГА ===");
            sb.AppendLine(contextResult.SummaryText);
            sb.AppendLine($"[Заменено {contextResult.ReplacedMessages} сообщений, отправлено {contextResult.SystemMessages.Count + contextResult.RecentMessages.Count}]");
            sb.AppendLine("==============================");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Извлекает новые факты из диалога и сохраняет в память.
    /// Использует LLM для анализа.
    /// </summary>
    private async void ExtractFactsFromDialogue(string userMessage, string assistantAnswer, IReadOnlyList<MemoryEntry>? dialogue = null)
    {
        try
        {
            var token = await _authClient.GetAccessTokenAsync();

            // Формируем текст полного диалога для анализа
            var dialogueText = string.Join("\n", dialogue?.Select(e => $"{e.Role}: {e.Content}") ?? new[] { $"{userMessage}\n{assistantAnswer}" });

            var extractionPrompt = $"""
                Извлеки важные факты из полного диалога ниже.
                Факты — это конкретная информация, которая может пригодиться в будущем.
                Не извлекай общие фразы, приветствия или вопросы.

                Формат: ключ: значение (однострочное описание)

                Полный диалог:
                {dialogueText}

                Извлеки факты в формате:
                ключ: значение
                ключ: значение

                Если фактов нет — напиши "нет фактов".
                """;

            var extractionMessages = new List<ApiMessage>
            {
                new() { Role = "user", Content = extractionPrompt },
            };

            var extractionRequest = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["messages"] = extractionMessages.Select(m => new { m.Role, m.Content }).ToList<object>(),
                ["stream"] = false,
            };

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            httpClient.BaseAddress = new Uri("https://api.giga.chat");

            var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", extractionRequest);

            if (response.IsSuccessStatusCode)
            {
                var parsed = await response.Content.ReadFromJsonAsync<ExtractionResponse>();
                if (parsed?.Choices?.Count > 0)
                {
                    var factsText = parsed.Choices[0].Message?.Content ?? "";

                    if (!string.IsNullOrWhiteSpace(factsText) && factsText != "нет фактов")
                    {
                        var lines = factsText.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                        var extractedCount = 0;
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            var colonIndex = trimmed.IndexOf(':');
                            if (colonIndex > 0 && colonIndex < trimmed.Length - 1)
                            {
                                var key = trimmed[..colonIndex].Trim();
                                var value = trimmed[(colonIndex + 1)..].Trim();

                                if (key.Length > 0 && value.Length > 0)
                                {
                                    // Сохраняем факты в активную стратегию
                                    SaveExtractedFact(key, value);
                                    extractedCount++;
                                }
                            }
                        }
                        Logger.Info($"Извлечение фактов: найдено {extractedCount} факт(ов)");
                    }
                    else
                    {
                        Logger.Info("Извлечение фактов: LLM не нашёл фактов для извлечения");
                    }
                }
                else
                {
                    Logger.Info("Извлечение фактов: пустой ответ от LLM");
                }
            }
            else
            {
                Logger.Warning($"Извлечение фактов: ошибка API HTTP {response.StatusCode}");
            }

            httpClient.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warning($"Ошибка извлечения фактов: {ex.Message}");
        }
    }

    /// <summary>
    /// Явно извлекает факты из полного диалога (для команд CLI).
    /// </summary>
    public async Task<int> ExtractFactsExplicitAsync(IReadOnlyList<MemoryEntry>? dialogue = null)
    {
        try
        {
            var token = await _authClient.GetAccessTokenAsync();

            // Формируем текст полного диалога для анализа
            var dialogueText = string.Join("\n", dialogue?.Select(e => $"{e.Role}: {e.Content}") ?? Array.Empty<string>());

            var extractionPrompt = $"""
                Извлеки важные факты из полного диалога ниже. Формат: ключ: значение.
                Не извлекай вопросы, приветствия и общие фразы.
                Если фактов нет — напиши "нет фактов".

                Полный диалог:
                {dialogueText}
                """;

            var extractionMessages = new List<ApiMessage>
            {
                new() { Role = "user", Content = extractionPrompt },
            };

            var extractionRequest = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["messages"] = extractionMessages.Select(m => new { m.Role, m.Content }).ToList<object>(),
                ["stream"] = false,
            };

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            httpClient.BaseAddress = new Uri("https://api.giga.chat");

            var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", extractionRequest);
            httpClient.Dispose();

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning($"Извлечение фактов: ошибка API HTTP {response.StatusCode}");
                return 0;
            }

            var parsed = await response.Content.ReadFromJsonAsync<ExtractionResponse>();
            if (parsed is null || parsed.Choices is null || parsed.Choices.Count == 0) return 0;

            var factsText = parsed.Choices[0].Message?.Content ?? "";
            if (string.IsNullOrWhiteSpace(factsText) || factsText == "нет фактов") return 0;

            var lines = factsText.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            var extractedCount = 0;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var colonIndex = trimmed.IndexOf(':');
                if (colonIndex > 0 && colonIndex < trimmed.Length - 1)
                {
                    var key = trimmed[..colonIndex].Trim();
                    var value = trimmed[(colonIndex + 1)..].Trim();
                    if (key.Length > 0 && value.Length > 0)
                    {
                        SaveExtractedFact(key, value);
                        extractedCount++;
                    }
                }
            }
            return extractedCount;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Ошибка извлечения фактов: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Сохраняет извлечённый факт в активную стратегию (Branching/StickyFacts)
    /// и в долгосрочную память.
    /// Рабочая память заполняется отдельно — данными задачи (план, подзадачи, результаты).
    /// </summary>
    private void SaveExtractedFact(string key, string value)
    {
        // Всегда сохраняем в долгосрочную память
        MemoryManager.LongTerm.Save(key, value, "extracted");

        var strategy = ContextManager.Config.Strategy;

        switch (strategy)
        {
            case ContextStrategy.Branching when ContextManager.Branching is { } branching:
                branching.SaveFact(key, value);
                Logger.Info($"Факт сохранён: ветка ({branching.ActiveBranchName}) + LongTerm: {key}");
                break;

            case ContextStrategy.StickyFacts when ContextManager.StickyFacts is { } stickyFacts:
                stickyFacts.SaveFact(key, value);
                Logger.Info($"Факт сохранён: StickyFacts + LongTerm: {key}");
                break;

            default:
                Logger.Info($"Факт сохранён (LongTerm): {key}");
                break;
        }
    }

    /// <summary>
    /// Очищает историю диалога, кэш, память и контекст.
    /// </summary>
    public void ClearHistory()
    {
        _history.Clear();
        Cache.Clear();
        MemoryManager.ShortTerm.Clear();
        MemoryManager.LongTerm.Clear();
        ContextManager.Clear();
        Logger.Info("История, кэш, память и контекст очищены");
    }

    /// <summary>
    /// Загружает историю диалога из внешнего источника.
    /// </summary>
    public void LoadHistory(IEnumerable<ApiMessage> messages)
    {
        _history.Clear();
        foreach (var msg in messages)
        {
            _history.Add(msg);
        }
        // Загружаем в ContextManager — он формирует контекст для API
        ContextManager.LoadHistory(messages);
        Logger.Info($"Загружено {messages.Count()} сообщений истории");
    }

    /// <summary>
    /// Вспомогательный метод для обрезки строк.
    /// </summary>
    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 3)] + "...";
    }

    /// <summary>
    /// Выводит сохранённые факты из последнего действия.
    /// </summary>
    private void PrintSavedFacts()
    {
        var changes = MemoryManager.LongTerm.GetRecentChanges();
        if (changes.Count == 0)
            return;

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("🧠 Сохранённые факты:");
        Console.ResetColor();

        foreach (var change in changes)
        {
            var icon = change.WasNew ? "✨" : "🔄";
            Console.ForegroundColor = change.WasNew ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine($"   {icon} {change.Key}: {change.Value}");
            Console.ResetColor();
        }
    }
}
