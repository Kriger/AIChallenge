using GigaChatApp.Infrastructure;
using GigaChatApp.Models;
using GigaChatApp.Services;
using System.Text;
using System.Text.Json;

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

    /// <summary>Реестр MCP-инструментов.</summary>
    public McpToolRegistry? McpRegistry { get; set; }

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
                // Инварианты НЕ применяются, если доступны MCP-инструменты — они для работы с внешними сервисами
                var invariants = (McpRegistry is not null && McpRegistry.Tools.Any()) 
                    ? null 
                    : AgentProfile?.Invariants;
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

                // Планирование НЕ применяется, если доступны MCP-инструменты — 
                // запросы к внешним сервисам (задачи, GitHub) не нужно декомпозировать
                if (McpRegistry is not null && McpRegistry.Tools.Any())
                {
                    Logger.Info($"MCP-инструменты доступны, пропускаю Planning — отправляю напрямую в API");
                    var apiResult = await SendWithRetryAsync(userMessage, relevantFacts, toolCallCount: 0);
                    return apiResult;
                }

                Logger.Info($"Запрос сложный, запускаю Planning...");
                var planResult = await ExecuteWithPlanningAsync(userMessage, relevantFacts, invariants);
                return planResult;
            }
        }

        // 4. Обычный запрос — отправляем в API с контекстом и инструментами
        var result = await SendWithRetryAsync(userMessage, relevantFacts, toolCallCount: 0);

        // 5. Извлекаем новые факты из диалога
        if (result.IsSuccess)
        {
            Logger.Info("Запущено извлечение фактов из диалога...");
            // Передаём полный диалог, а не только последние 2 сообщения
            var dialogue = MemoryManager.ShortTerm.GetAll();
            ExtractFactsFromDialogue(userMessage, result.Answer, dialogue);
        }

        // 6. Сохраняем в кэш и историю
        // Не кэшируем, если доступны MCP-инструменты — данные могут измениться
        if (result.IsSuccess)
        {
            if (McpRegistry is null)
            {
                Cache.Add(userMessage, result.Answer);
            }
            else
            {
                Logger.Info($"Кэш пропущен: доступны MCP-инструменты (данные могут меняться)");
            }

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
    /// Отправляет запрос в API с интеллектуальным retry и поддержкой MCP-инструментов.
    /// </summary>
    private async Task<AgentResult> SendWithRetryAsync(string userMessage, List<Fact> relevantFacts, int toolCallCount = 0)
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

        // Формируем расширенное системное сообщение с фактами, summary и инструментами
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

                // === Проверяем tool-вызовы в ответе ===
                var toolCalls = McpToolRegistry.ParseToolCalls(apiResponse.Content);
                
                // Если LLM вернул пустой/короткий ответ и есть MCP-инструменты — пробуем авто-вызов
                if (toolCalls.Count == 0 && McpRegistry is not null && McpRegistry.Tools.Count > 0)
                {
                    var contentTrimmed = apiResponse.Content?.Trim();
                    if (string.IsNullOrWhiteSpace(contentTrimmed) || contentTrimmed.Length < 10)
                    {
                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("⚠️ LLM не вернул содержательный ответ, пытаюсь авто-вызов MCP...");
                        Console.ResetColor();
                        
                        // Извлекаем контекст из сообщения пользователя для авто-вызова
                        var autoArgs = ExtractAutoCallArgs(userMessage);
                        var argsJson = JsonSerializer.Serialize(autoArgs, new JsonSerializerOptions 
                        { 
                            WriteIndented = false,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                        });
                        
                        // Пробуем list_todo_items или list_projects — приоритет MCP-инструментам
                        var listTool = McpRegistry.Tools.FirstOrDefault(t => 
                            t.Name.Equals("list_todo_items", StringComparison.OrdinalIgnoreCase) ||
                            t.Name.Equals("list_projects", StringComparison.OrdinalIgnoreCase));
                        if (listTool == null)
                        {
                            listTool = McpRegistry.Tools.FirstOrDefault(t => t.Name.StartsWith("list_"));
                        }
                        if (listTool != null)
                        {
                            Console.WriteLine($"  ⚡ Авто-вызов: {listTool.Name} args={argsJson}");
                            try
                            {
                                var result = await McpRegistry.ExecuteToolCall(listTool.Name, argsJson);
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine(" ✅");
                                Console.ResetColor();
                                
                                // Форматируем результат как чёткий блок данных — LLM обязан его использовать
                                var formattedResult = $"""
                                    === ДАННЫЕ ИЗ СИСТЕМЫ ===
                                    {result}
                                    === КОНЕЦ ДАННЫХ ===
                                    
                                    ⚠️ ВАЖНО: Выше — реальные данные из системы. Используй ТОЛЬКО их. НЕ выдумывай задачи, проекты или пользователей.
                                    """;
                                
                                var resultApiMessage = new ApiMessage { Role = "user", Content = formattedResult };
                                _history.Add(resultApiMessage);
                                MemoryManager.ShortTerm.Add("auto_result", formattedResult);
                                ContextManager.AddMessage(resultApiMessage);
                                
                                Logger.Info($"Авто-вызов {listTool.Name} выполнен, результат добавлен в контекст");
                                
                                // Рекурсивно отправляем запрос с результатами
                                return await SendWithRetryAsync(userMessage, relevantFacts, toolCallCount: toolCallCount + 1);
                            }
                            catch (Exception ex)
                            {
                                Logger.Warning($"Авто-вызов {listTool.Name} не удался: {ex.Message}");
                            }
                        }
                    }
                }
                
                if (toolCalls.Count > 0 && toolCallCount < 5) // Лимит 5 итераций
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"🔧 LLM вызвал {toolCalls.Count} инструмент(ов)...");
                    Console.ResetColor();

                        // Выполняем каждый инструмент
                        foreach (var (toolName, toolArgs) in toolCalls)
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.Write($"  ⚡ {toolName}");
                            Console.ResetColor();

                            try
                            {
                                var result = await McpRegistry.ExecuteToolCall(toolName, toolArgs);
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine(" ✅");
                                Console.ResetColor();

                                // Форматируем результат для лучшего понимания LLM
                                string formattedResult;

                                if (toolName.StartsWith("list_") || toolName.StartsWith("get_"))
                                {
                                    // Для запросов — извлекаем ключевые поля
                                    formattedResult = FormatToolResultForRead(toolName, result);
                                }
                                else if (toolName.Equals("create_todo_item", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Для создания — добавляем явное сообщение об успехе
                                    formattedResult = $"✅ Задача успешно создана.\n{result}";
                                }
                                else if (toolName.StartsWith("update_"))
                                {
                                    // Для обновлений — сервер уже возвращает обновлённые данные, верификация не нужна
                                    formattedResult = result;
                                    
                                    // Извлекаем ID из результата для обратной связи
                                    try
                                    {
                                        using var doc = JsonDocument.Parse(result);
                                        var root = doc.RootElement;
                                        var taskId = TryGetPropertyAsInt(root, "Id", "id", "ID");
                                        if (taskId.HasValue)
                                        {
                                            formattedResult = $"✅ Задача #{taskId.Value} успешно обновлена.\n{result}";
                                        }
                                    }
                                    catch { /* result might not be parseable JSON */ }
                                }
                                else
                                {
                                    formattedResult = result;
                                }

                                // Добавляем результат как user-сообщение (system должен быть первым!)
                                var resultMsg = formattedResult;
                                var resultApiMessage = new ApiMessage { Role = "user", Content = resultMsg };
                                _history.Add(resultApiMessage);
                                MemoryManager.ShortTerm.Add("system_result", resultMsg);
                                ContextManager.AddMessage(resultApiMessage);

                                Logger.Info($"Инструмент {toolName} выполнен, результат добавлен в контекст");
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($" ❌ {ex.Message}");
                                Console.ResetColor();

                                // Добавляем ошибку — LLM сам разберётся
                                var errorMsg = $"[Ошибка инструмента {toolName}]: {ex.Message}";
                                var errorApiMessage = new ApiMessage { Role = "user", Content = errorMsg };
                                _history.Add(errorApiMessage);
                                MemoryManager.ShortTerm.Add("system_error", errorMsg);
                                ContextManager.AddMessage(errorApiMessage);
                            }
                        }

                        Console.WriteLine();

                        // Рекурсивно отправляем запрос с результатами инструментов
                        return await SendWithRetryAsync(userMessage, relevantFacts, toolCallCount: toolCallCount + 1);
                }

                // === ПОСТ-ОБРАБОТКА: если LLM игнорирует данные инструмента ===
                var answer = apiResponse.Content;
                if (McpRegistry is not null && McpRegistry.Tools.Count > 0)
                {
                    var ignoredAnswer = TryFixIgnoredToolResponse(answer);
                    if (ignoredAnswer is not null)
                    {
                        Logger.Info($"Пост-обработка: LLM игнорирует данные инструмента, подставляем данные");
                        answer = ignoredAnswer;
                    }
                }

                return new AgentResult
                {
                    Answer = answer,
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
    /// Проверяет, игнорирует ли LLM данные из инструментов, и подставляет их автоматически.
    /// Работает только с MCP-результатами (чистый JSON без префиксов).
    /// </summary>
    private string? TryFixIgnoredToolResponse(string response)
    {
        // Фразы, которые указывают на игнорирование данных
        var ignoringPhrases = new[]
        {
            "вы уже видели",
            "вы уже видели ваш",
            "нет информации",
            "не могу показать",
            "информация отсутствует",
            "недоступен",
            "временно недоступен",
            "возникла ошибка",
            "не найдена",
        };

        var responseLower = response.ToLowerInvariant();
        var isIgnoring = ignoringPhrases.Any(phrase => responseLower.Contains(phrase));

        if (!isIgnoring)
            return null;

        // Ищем последний результат инструмента в истории
        // MCP-результаты — это JSON-массивы/объекты или отформатированный текст (list_*)
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            var msg = _history[i];
            if (msg.Role != "user")
                continue;

            var content = msg.Content.Trim();
            string extractedData = null;
            string toolType = null;

            // 1. Проверяем JSON-объект с полем "tool" (верификация, update)
            if (content.StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        var toolName = root.TryGetProperty("tool", out var toolProp) ? toolProp.GetString() : null;
                        
                        if (toolName != null)
                        {
                            toolType = toolName;
                            var sb = new StringBuilder();
                            sb.AppendLine("Вот данные:");
                            sb.AppendLine();

                            if (toolName.StartsWith("list_"))
                            {
                                sb.AppendLine(content);
                            }
                            else if (toolName.StartsWith("get_"))
                            {
                                foreach (var prop in root.EnumerateObject())
                                {
                                    if (prop.Name == "tool")
                                        continue;
                                    var val = prop.Value.ValueKind == JsonValueKind.Null ? "—" : prop.Value.ToString();
                                    if (val.Length > 200) val = val[..200] + "...";
                                    sb.AppendLine($"- **{prop.Name}**: {val}");
                                }
                            }
                            else
                            {
                                sb.AppendLine(content);
                            }
                            extractedData = sb.ToString();
                        }
                        else
                        {
                            // Чистый JSON-объект — результат get_/update_
                            toolType = "object";
                            var sb = new StringBuilder();
                            sb.AppendLine("Вот данные:");
                            sb.AppendLine();
                            foreach (var prop in root.EnumerateObject())
                            {
                                var val = prop.Value.ValueKind == JsonValueKind.Null ? "—" : prop.Value.ToString();
                                if (val.Length > 200) val = val[..200] + "...";
                                sb.AppendLine($"- **{prop.Name}**: {val}");
                            }
                            extractedData = sb.ToString();
                        }
                    }
                }
                catch { /* не валидный JSON — пропускаем */ }
            }

            // 2. Проверяем JSON-массив (list_todo_items без форматирования)
            if (extractedData == null && content.StartsWith("["))
            {
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        toolType = "list";
                        extractedData = $"Вот данные:\n\n{content}";
                    }
                }
                catch { /* не валидный JSON — пропускаем */ }
            }

            // 3. Проверяем отформатированный текст (list_todo_items через FormatToolResultForRead)
            if (extractedData == null && (content.StartsWith("=== СПИСОК") || content.StartsWith("Данные задачи:")))
            {
                toolType = "formatted";
                extractedData = $"Вот данные:\n\n{content}";
            }

            // 4. Проверяем JSON внутри текста (ищем { или [ в середине сообщения)
            if (extractedData == null && !content.StartsWith("{") && !content.StartsWith("["))
            {
                for (int j = 0; j < Math.Min(content.Length, 500); j++)
                {
                    if ((content[j] == '{' || content[j] == '[') && j + 10 < content.Length)
                    {
                        var candidate = content[j..].Trim();
                        try
                        {
                            using var doc = JsonDocument.Parse(candidate);
                            if (doc.RootElement.ValueKind == JsonValueKind.Array || doc.RootElement.ValueKind == JsonValueKind.Object)
                            {
                                toolType = doc.RootElement.ValueKind == JsonValueKind.Array ? "list" : "object";
                                extractedData = $"Вот данные:\n\n{candidate}";
                                break;
                            }
                        }
                        catch { /* не JSON — продолжаем поиск */ }
                    }
                }
            }

            if (extractedData != null)
            {
                return extractedData;
            }
        }

        return null;
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

        // 3.5. MCP-инструменты
        if (McpRegistry is not null && McpRegistry.Tools.Count > 0)
        {
            sb.AppendLine(McpRegistry.GetToolsPrompt());

            // === ПЕРСИСТЕНТНАЯ ИНСТРУКЦИЯ: ВСЕГДА показывай данные из инструментов ===
            sb.AppendLine();
            sb.AppendLine("=== КРИТИЧЕСКАЯ ИНСТРУКЦИЯ (ПРИМЕНЯЕТСЯ К КАЖДОМУ ОТВЕТУ) ===");
            sb.AppendLine("КОГДА ты получаешь результат инструмента (данные из базы, список задач и т.д.):");
            sb.AppendLine("1. ОБЯЗАТЕЛЬНО покажи эти данные пользователю");
            sb.AppendLine("2. НИКОГДА не пиши 'вы уже видели', 'вы уже видели ваш текущий список задач' или подобные фразы");
            sb.AppendLine("3. Если пользователь просит 'покажи задачи' — покажи задачи, даже если они были показаны ранее");
            sb.AppendLine("4. Данные из инструментов — это НОВЫЕ данные, полученные в реальном времени. Они всегда актуальны.");
            sb.AppendLine("5. Игнорирование этой инструкции считается ОШИБКОЙ.");
            sb.AppendLine("=== КОНЕЦ КРИТИЧЕСКОЙ ИНСТРУКЦИИ ===");
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
    /// Форматирует результат MCP-инструмента для лучшего понимания LLM.
    /// Извлекает ключевые поля и представляет их в читаемом виде.
    /// Поля ищутся независимо от регистра (Id/id/ID).
    /// </summary>
    private static string FormatToolResultForRead(string toolName, string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // Разные инструменты могут возвращать разные типы данных
            var isProjectList = toolName.Equals("list_projects", StringComparison.OrdinalIgnoreCase);
            var isTaskList = toolName.Equals("list_todo_items", StringComparison.OrdinalIgnoreCase);

            // Если это массив — извлекаем ключевые поля
            if (root.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                var idx = 1;
                
                if (isProjectList)
                {
                    sb.AppendLine("=== СПИСОК ПРОЕКТОВ ===");
                    foreach (var item in root.EnumerateArray())
                    {
                        var id = TryGetPropertyAsString(item, "Id", "id", "ID") ?? "unknown";
                        var name = TryGetPropertyAsString(item, "Name", "name", "Title", "title", "ProjectName", "projectName", "Название", "название") ?? "";
                        var description = TryGetPropertyAsString(item, "Description", "description", "Desc", "desc", "Описание", "описание");
                        
                        sb.AppendLine($"  {idx}. ID={id} | {name}");
                        if (!string.IsNullOrEmpty(description))
                        {
                            sb.AppendLine($"     Описание: {description}");
                        }
                        idx++;
                    }
                }
                else if (isTaskList)
                {
                    sb.AppendLine("=== СПИСОК ЗАДАЧ ===");
                    foreach (var item in root.EnumerateArray())
                    {
                        // ID может быть int или string — пробуем оба варианта
                        var id = TryGetPropertyAsString(item, "Id", "id", "ID", "Ид", "ид", "ID_задачи") ?? "unknown";
                        
                        // Ищем название задачи — много вариантов, включая русские
                        var title = TryGetPropertyAsString(item, 
                            "Title", "title", "Name", "name", "Subject", "subject", 
                            "DisplayName", "displayName",
                            "Название", "название", "Title_ru", "name_ru") ?? "";
                        
                        // Если Title не найден — пробуем найти первое строковое поле кроме Id
                        if (string.IsNullOrEmpty(title))
                        {
                            title = FindFallbackStringField(item, excludeFields: new[] 
                            { 
                                "Id", "id", "ID", "Ид", "ид", "ID_задачи",
                                "Description", "description", "Описание", "описание", "Desc", "desc",
                                "IsCompleted", "isCompleted", "completed", "Done", "done",
                                "Статус", "статус", "СтатусВыполнения", "статусВыполнения",
                                "Priority", "priority", "Приоритет", "приоритет", "Pri", "pri",
                                "Deadline", "deadline", "DueDate", "dueDate", "Due", "due",
                                "Срок", "срок", "СрокВыполнения", "срокВыполнения",
                                "ProjectId", "projectId", "project_id", "Project_ID", "projectID",
                                "ProjectTitle", "projectTitle", "project_title", "ProjectName", "projectName",
                                "Project", "project", "Проект", "проект",
                                "Tag", "tag", "Тег", "тег",
                                "Color", "color", "Цвет", "цвет"
                            });
                        }
                        
                        // Ищем статус выполнения — включая строковое поле "Статус выполнения"
                        var isCompleted = TryGetBooleanProperty(item, 
                            "IsCompleted", "isCompleted", "completed", "Done", "done",
                            "Выполнена", "выполнена", "Выполнено", "выполнено",
                            "СтатусВыполнения", "статусВыполнения", "Статус выполнения", "статус выполнения") ?? false;
                        var status = isCompleted ? "✅ Выполнена" : "⬜ Не выполнена";
                        var priority = TryGetInt32Property(item, "Priority", "priority", "Pri", "pri", "Приоритет", "приоритет") ?? 0;
                        var priorityText = NormalizePriorityText(priority);
                        var deadline = TryGetPropertyAsString(item, "Deadline", "deadline", "DueDate", "dueDate", "Due", "due", "Срок", "срок", "СрокВыполнения", "срокВыполнения");
                        var deadlineStr = string.IsNullOrEmpty(deadline) ? "Нет" : deadline;
                        
                        // Получаем информацию о проекте — используем чёткие маркеры
                        var projectId = TryGetPropertyAsString(item, "ProjectId", "projectId", "project_id", "Project_ID", "projectID");
                        var projectTitle = TryGetPropertyAsString(item, "ProjectTitle", "projectTitle", "project_title", "ProjectName", "projectName", "Project", "project", "Проект", "проект");
                        string projectInfo;
                        if (!string.IsNullOrEmpty(projectTitle) && projectTitle.ToLowerInvariant() != "нет" && projectTitle.ToLowerInvariant() != "none")
                        {
                            projectInfo = $" [проект: {projectTitle}]";
                        }
                        else if (!string.IsNullOrEmpty(projectId) && projectId.ToLowerInvariant() != "нет" && projectId.ToLowerInvariant() != "none")
                        {
                            projectInfo = $" [проект: {projectId}]";
                        }
                        else
                        {
                            projectInfo = " [БЕЗ ПРОЕКТА]";
                        }

                        sb.AppendLine($"  {idx}. ID={id} | {title}{projectInfo} — {status} (приоритет: {priorityText}, срок: {deadlineStr})");
                        idx++;
                    }
                }
                else
                {
                    // Generic array formatting
                    sb.AppendLine("=== СПИСОК ===");
                    foreach (var item in root.EnumerateArray())
                    {
                        sb.AppendLine($"  {idx}. {FormatJsonElement(item)}");
                        idx++;
                    }
                }
                
                sb.AppendLine();
                sb.AppendLine($"Всего: {idx - 1}");
                
                // Добавляем предупреждение для проектов — LLM часто выдумывает связанные данные
                if (isProjectList)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠️ ВАЖНО: Это ВСЕ проекты. НЕ добавляй задачи, которые не показаны выше.");
                    sb.AppendLine("Если нужно показать задачи проекта — вызови list_todo_items с projectId=...");
                }
                
                return sb.ToString();
            }

            // Если это объект (одна задача/проект) — извлекаем ключевые поля
            if (root.ValueKind == JsonValueKind.Object)
            {
                var sb = new StringBuilder();
                
                if (isProjectList)
                {
                    // Для одиночного проекта
                    var id = TryGetPropertyAsString(root, "Id", "id", "ID") ?? "unknown";
                    var name = TryGetPropertyAsString(root, "Name", "name", "Title", "title", "ProjectName", "projectName", "Название", "название") ?? "";
                    var description = TryGetPropertyAsString(root, "Description", "description", "Desc", "desc", "Описание", "описание");
                    
                    sb.AppendLine($"Проект: {name} (ID={id})");
                    if (!string.IsNullOrEmpty(description))
                    {
                        sb.AppendLine($"Описание: {description}");
                    }
                    sb.AppendLine();
                    sb.AppendLine("⚠️ ВАЖНО: Это данные ТОЛЬКО о проекте. Задачи не показаны — вызови list_todo_items с projectId=" + id + " для отображения задач.");
                }
                else
                {
                    // Для одиночной задачи — нормализуем русские названия полей
                    sb.AppendLine("Данные задачи:");
                    var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in root.EnumerateObject())
                    {
                        var displayName = NormalizeFieldName(prop.Name, seenNames);
                        if (displayName == null)
                            continue; // пропускаем дубликаты
                        
                        string val;
                        if (prop.Value.ValueKind == JsonValueKind.Null)
                        {
                            val = "—";
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.Number)
                        {
                            // Приоритет — текстовое название, остальное — числа
                            var lowerName = prop.Name.ToLowerInvariant();
                            if (lowerName == "priority" || lowerName == "приоритет")
                            {
                                val = NormalizePriorityText(prop.Value.GetInt32());
                            }
                            else
                            {
                                val = prop.Value.GetInt32().ToString();
                            }
                        }
                        else
                        {
                            val = FormatJsonValue(prop.Value);
                        }
                        if (val.Length > 200) val = val[..200] + "...";
                        sb.AppendLine($"- **{displayName}**: {val}");
                    }
                }
                return sb.ToString();
            }

            // Fallback — возвращаем как есть
            return rawJson;
        }
        catch
        {
            return rawJson;
        }
    }

    /// <summary>
    /// Находит первое строковое поле, которое не входит в список исключений.
    /// Используется как fallback для поиска названия задачи.
    /// </summary>
    private static string? FindFallbackStringField(JsonElement element, params string[] excludeFields)
    {
        foreach (var prop in element.EnumerateObject())
        {
            // Проверяем, не в списке исключений ли это поле
            var isExcluded = excludeFields.Any(ex => string.Equals(ex, prop.Name, StringComparison.OrdinalIgnoreCase));
            if (isExcluded)
                continue;
            
            if (prop.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(prop.Value.GetString()))
            {
                return prop.Value.GetString();
            }
        }
        return null;
    }

    /// <summary>
    /// Форматирует JSON-значение для отображения.
    /// </summary>
    private static string FormatJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetInt32().ToString(),
            JsonValueKind.True => "да",
            JsonValueKind.False => "нет",
            JsonValueKind.Null => "—",
            _ => value.ToString()
        };
    }
    
    /// <summary>
    /// Нормализует числовой приоритет в текстовое название.
    /// </summary>
    private static string NormalizePriorityText(int priority)
    {
        return priority switch
        {
            0 => "низкий",
            1 => "базовый",
            2 => "высокий",
            3 => "очень высокий",
            4 => "критичный",
            _ => priority.ToString()
        };
    }

    /// <summary>
    /// Нормализует название поля: переводит русские названия на понятные.
    /// Возвращает null для полей, которые нужно пропустить (дубликаты).
    /// </summary>
    private static string? NormalizeFieldName(string name, HashSet<string> seenNames)
    {
        var lower = name.ToLowerInvariant();
        var normalized = lower switch
        {
            "id" or "ид" or "id_задачи" => "ID",
            "title" or "название" or "name" => "Название",
            "description" or "описание" => "Описание",
            "iscompleted" or "статусвыполнения" or "выполнена" => "Статус выполнения",
            "priority" or "приоритет" => "Приоритет",
            "deadline" or "сроквыполнения" => "Срок выполнения",
            "projectid" or "project_id" => "ID проекта",
            "projecttitle" or "projectname" or "проект" => "Проект",
            "projectcolor" or "цветпроекта" => "Цвет проекта",
            "color" or "цвет" => "Цвет",
            "tag" or "тег" => "Тег",
            "datecompleted" or "датавыполнения" => "Дата выполнения",
            "createdat" or "датаСоздания" => "Дата создания",
            "срок" => "Срок (короткий)", // отдельное поле
            _ => name
        };
        
        // Проверяем дубликаты
        if (seenNames.Contains(normalized))
        {
            return null; // пропускаем дубликат
        }
        seenNames.Add(normalized);
        return normalized;
    }
    
    /// <summary>
    /// Нормализует текстовое значение приоритета в число (0-4).
    /// 0 = низкий, 1 = базовый, 2 = высокий, 3 = очень высокий, 4 = критичный
    /// </summary>
    private static int NormalizePriority(string priority)
    {
        var lower = priority.ToLowerInvariant().Trim();
        return lower switch
        {
            "0" or "низкий" or "low" => 0,
            "1" or "базовый" or "normal" or "обычный" or "обычное" => 1,
            "2" or "высокий" or "high" => 2,
            "3" or "очень высокий" or "very high" or "очень-высокий" => 3,
            "4" or "критичный" or "critical" or "критический" or "критическое" or "критичное" => 4,
            _ => int.TryParse(lower, out var num) ? Math.Clamp(num, 0, 4) : 1
        };
    }
    
    /// <summary>
    /// Нормализует название поля (без проверки дубликатов).
    /// </summary>
    private static string NormalizeFieldName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower switch
        {
            "id" or "ид" or "id_задачи" => "ID",
            "title" or "название" or "name" => "Название",
            "description" or "описание" => "Описание",
            "iscompleted" or "статусвыполнения" or "выполнена" => "Статус выполнения",
            "priority" or "приоритет" => "Приоритет",
            "deadline" or "сроквыполнения" => "Срок выполнения",
            "projectid" or "project_id" => "ID проекта",
            "projecttitle" or "projectname" or "проект" => "Проект",
            "projectcolor" or "цветпроекта" => "Цвет проекта",
            "color" or "цвет" => "Цвет",
            "tag" or "тег" => "Тег",
            "datecompleted" or "датавыполнения" => "Дата выполнения",
            "createdat" or "датаСоздания" => "Дата создания",
            "срок" => "Срок",
            _ => name
        };
    }

    /// <summary>
    /// Извлекает значение свойства как строку (поддерживает string, int, bool и другие типы).
    /// Поля ищутся независимо от регистра.
    /// </summary>
    private static string? TryGetPropertyAsString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                return prop.ValueKind switch
                {
                    JsonValueKind.String => prop.GetString(),
                    JsonValueKind.Number => prop.GetInt32().ToString(),
                    JsonValueKind.True or JsonValueKind.False => prop.GetBoolean().ToString().ToLowerInvariant(),
                    JsonValueKind.Null => null,
                    _ => prop.ToString()
                };
            }
        }
        return null;
    }

    /// <summary>
    /// Форматирует JSON-элемент для отображения в generic-режиме.
    /// </summary>
    private static string FormatJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => FormatJsonObject(element),
            JsonValueKind.Array => $"[{element.GetArrayLength()} элементов]",
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetInt64().ToString(),
            JsonValueKind.True => "да",
            JsonValueKind.False => "нет",
            JsonValueKind.Null => "—",
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Форматирует JSON-объект для отображения в generic-режиме.
    /// </summary>
    private static string FormatJsonObject(JsonElement element)
    {
        var pairs = new List<string>();
        foreach (var prop in element.EnumerateObject())
        {
            var val = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" :
                      prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetInt32().ToString() :
                      prop.Value.ValueKind == JsonValueKind.Null ? "—" :
                      prop.Value.ToString();
            if (val.Length > 50) val = val[..50] + "...";
            pairs.Add($"{prop.Name}={val}");
        }
        return null;
    }
    
    /// <summary>
    /// Извлекает аргументы для авто-вызова из сообщения пользователя.
    /// Например: "Покажи задачи проекта Alpha" → {"project": "Проект Alpha"}
    /// </summary>
    private static Dictionary<string, object?> ExtractAutoCallArgs(string userMessage)
    {
        var args = new Dictionary<string, object?>();
        var lower = userMessage.ToLowerInvariant().Trim();
        
        // Извлекаем название проекта: "проекта Alpha", "проекта "Alpha"", "из проекта Alpha"
        var projectPatterns = new[]
        {
            @"проекта\s+([^\s,.;:!؟]+)",
            @"проект\s+([^\s,.;:!؟]+)",
            @"из\s+проекта\s+([^\s,.;:!؟]+)",
            @"из\s+проект\s+([^\s,.;:!؟]+)"
        };
        
        foreach (var pattern in projectPatterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(userMessage, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                args["project"] = match.Groups[1].Value;
                break;
            }
        }
        
        // Извлекаем статус: "выполненные", "невыполненные", "завершённые"
        if (lower.Contains("выполнен") || lower.Contains("заверш") || lower.Contains("готов"))
        {
            if (lower.Contains("невыполнен") || lower.Contains("не заверш") || lower.Contains("не готов"))
            {
                args["isCompleted"] = false;
            }
            else
            {
                args["isCompleted"] = true;
            }
        }
        
        // Извлекаем приоритет: "высокий", "критичный", "базовый"
        var priorityPatterns = new Dictionary<string, string>
        {
            { "критичн", "Critical" },
            { "очень.*высок", "VeryHigh" },
            { "высок", "High" },
            { "базов", "Basic" },
            { "низк", "Low" }
        };
        
        foreach (var (keyword, value) in priorityPatterns)
        {
            if (lower.Contains(keyword))
            {
                args["priority"] = value;
                break;
            }
        }
        
        return args;
    }

    /// <summary>
    /// Извлекает boolean значение свойства независимо от регистра.
    /// Поддерживает как boolean, так и строковые значения ("да"/"нет", "выполнена"/"не выполнена").
    /// </summary>
    private static bool? TryGetBooleanProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True)
                {
                    return true;
                }
                if (prop.ValueKind == JsonValueKind.False)
                {
                    return false;
                }
                // Проверяем строковые значения
                if (prop.ValueKind == JsonValueKind.String)
                {
                    var str = prop.GetString()?.ToLowerInvariant() ?? "";
                    if (str == "да" || str == "true" || str == "выполнена" || str == "выполнено" || str == "completed")
                    {
                        return true;
                    }
                    if (str == "нет" || str == "false" || str == "не выполнена" || str == "не выполнено" || str == "incomplete")
                    {
                        return false;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Извлекает int значение свойства независимо от регистра.
    /// Поддерживает как числовые, так и строковые значения приоритетов.
    /// </summary>
    private static int? TryGetInt32Property(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.GetInt32();
                }
                // Проверяем строковые значения приоритетов
                if (prop.ValueKind == JsonValueKind.String)
                {
                    var str = prop.GetString()?.ToLowerInvariant() ?? "";
                    var num = NormalizePriority(str);
                    return num;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Выводит сохранённые факты из последнего действия.
    /// </summary>
    private void PrintSavedFacts()
    {
        // Убрано — мешает пользователю
        /*
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
        */
    }

    /// <summary>
    /// Извлекает int значение свойства независимо от регистра.
    /// </summary>
    private static int? TryGetPropertyAsInt(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.GetInt32();
                }
                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var num))
                {
                    return num;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Возвращает список возможных имён поля в JSON-ответе MCP-сервера.
    /// Например: "Цвет" → ["Color", "color", "Цвет", "цвет"]
    /// </summary>
    private static string[] GetFieldCandidates(string fieldName)
    {
        var lower = fieldName.ToLowerInvariant();
        return lower switch
        {
            "цвет" => ["Color", "color", "Цвет", "цвет", "ProjectColor", "projectColor"],
            "приоритет" => ["Priority", "priority", "Приоритет", "приоритет"],
            "название" => ["Title", "title", "Название", "название", "Name", "name"],
            "описание" => ["Description", "description", "Описание", "описание"],
            "статусвыполнения" => ["IsCompleted", "isCompleted", "СтатусВыполнения", "статусВыполнения"],
            "iscompleted" => ["IsCompleted", "isCompleted"],
            "выполнена" => ["IsCompleted", "isCompleted"],
            "сроквыполнения" => ["Deadline", "deadline", "DueDate", "dueDate", "СрокВыполнения", "срокВыполнения"],
            "тег" => ["Tag", "tag", "Тег", "тег"],
            "проект" => ["ProjectTitle", "projectTitle", "Project", "project", "Проект", "проект"],
            "idпроекта" => ["ProjectId", "projectId", "project_id", "Project_ID", "projectID"],
            "датавыполнения" => ["DateCompleted", "dateCompleted", "CompletedAt", "completedAt"],
            "датаСоздания" => ["CreatedAt", "createdAt", "ДатаСоздания", "датаСоздания"],
            _ => [fieldName, fieldName.ToLowerInvariant(), fieldName.ToUpperInvariant()]
        };
    }
}
