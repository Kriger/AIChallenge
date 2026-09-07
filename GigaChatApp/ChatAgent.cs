using GigaChatApp.Infrastructure;
using GigaChatApp.Models;
using GigaChatApp.Services;

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

    private readonly List<ApiMessage> _history = new();

    /// <summary>Собственная история диалога.</summary>
    public IReadOnlyList<ApiMessage> History => _history;

    /// <summary>Долгосрочная память.</summary>
    public Memory Memory { get; }

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

    public ChatAgent(ChatClient httpClient, AuthClient authClient,
        RequestCache? cache = null, AgentLogger? logger = null, Memory? memory = null, AdaptiveBehavior? adaptive = null, Planner? planner = null)
    {
        _httpClient = httpClient;
        _authClient = authClient;
        Cache = cache ?? new RequestCache();
        Logger = logger ?? new AgentLogger();
        Memory = memory ?? new Memory(Logger);
        Adaptive = adaptive ?? new AdaptiveBehavior(Metrics, Cache, Logger);
        _planner = planner ?? new Planner(httpClient, authClient, Model, Logger, Memory);
        Planner = _planner;
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
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("   [из кэша]");
            Console.ResetColor();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("🤖 GigaChat:");
            Console.ResetColor();
            Console.WriteLine($"   {cachedAnswer}");
            Console.WriteLine();

            return new AgentResult
            {
                Answer = cachedAnswer,
                Source = Source.Cache,
            };
        }

        // 2. Ищем релевантные факты
        var relevantFacts = Memory.FindRelevant(userMessage);
        if (relevantFacts.Count > 0)
        {
            Logger.Info($"Память: найдено {relevantFacts.Count} релевантных факт(ов)");
            foreach (var fact in relevantFacts)
            {
                Logger.Debug($"  → {fact.Key}: {Truncate(fact.Value, 80)}");
            }
        }

        // 3. Проверяем, нужен ли Planning
        if (Planner.NeedsDecomposition(userMessage))
        {
            Logger.Info($"Запрос сложный, запускаю Planning...");
            var planResult = await ExecuteWithPlanningAsync(userMessage, relevantFacts);
            return planResult;
        }

        // 4. Обычный запрос — отправляем в API
        var result = await SendWithRetryAsync(userMessage, relevantFacts);

        // 5. Извлекаем новые факты из диалога
        if (result.IsSuccess)
        {
            ExtractFactsFromDialogue(userMessage, result.Answer);
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

        // 7. Адаптация на основе метрик
        Adaptive.Adapt();

        // 8. Выводим сохранённые факты
        PrintSavedFacts();

        return result;
    }

    /// <summary>
    /// Выполняет запрос через Planning — декомпозирует на подзадачи, выполняет, комбинирует.
    /// </summary>
    private async Task<AgentResult> ExecuteWithPlanningAsync(string userMessage, List<Fact> relevantFacts)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("📋 Создаю план выполнения...");
        Console.ResetColor();

        // Создаём план
        var plan = await Planner.CreatePlanAsync(userMessage, relevantFacts);
        if (plan is null || plan.Tasks.Count == 0)
        {
            Logger.Info("План не создан, обрабатываю как обычный запрос");
            return await SendWithRetryAsync(userMessage, relevantFacts);
        }

        Logger.Info($"Создан план с {plan.Tasks.Count} подзадачами");

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
        // Добавляем запрос в историю
        _history.Add(new ApiMessage { Role = "user", Content = userMessage });

        // Формируем расширенное системное сообщение с фактами
        var extendedSystemMessage = BuildExtendedSystemMessage(relevantFacts);

        var lastException = default(Exception);
        var retryCount = 0;

        for (var attempt = 0; attempt <= Metrics.RetryCount; attempt++)
        {
            try
            {
                var token = await _authClient.GetAccessTokenAsync();

                var apiResponse = await _httpClient.SendCompletionAsync(
                    Model,
                    _history,
                    MaxTokens > 0 ? MaxTokens : (int?)null,
                    Temperature,
                    StopSequences,
                    extendedSystemMessage,
                    token
                );

                return new AgentResult
                {
                    Answer = apiResponse.Content,
                    Duration = apiResponse.Duration,
                    Usage = apiResponse.Usage,
                    Source = Source.Api
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
    /// Формирует системное сообщение с релевантными фактами.
    /// </summary>
    private string BuildExtendedSystemMessage(List<Fact> relevantFacts)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(SystemMessage))
        {
            sb.AppendLine(SystemMessage);
        }

        if (relevantFacts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("=== КОНТЕКСТ ИЗ ПАМЯТИ ===");
            sb.AppendLine("В диалоге обсуждались следующие темы:");

            // Группируем факты по ключу, чтобы не дублировать
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fact in relevantFacts)
            {
                if (seenKeys.Add(fact.Key))
                {
                    sb.AppendLine($"- {fact.Key}: {fact.Value}");
                }
            }

            sb.AppendLine("Используйте эту информацию для более точного ответа.");
            sb.AppendLine("==========================");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Извлекает новые факты из диалога и сохраняет в память.
    /// Использует LLM для анализа.
    /// </summary>
    private async void ExtractFactsFromDialogue(string userMessage, string assistantAnswer)
    {
        try
        {
            var token = await _authClient.GetAccessTokenAsync();

            var extractionPrompt = $"""
                Извлеки важные факты из следующего диалога.
                Факты — это конкретная информация, которая может пригодиться в будущем.
                Не извлекай общие фразы, приветствия или вопросы.

                Формат: ключ: значение (однострочное описание)

                Пользователь: {userMessage}
                Ассистент: {assistantAnswer}

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
                                    Memory.Save(key, value, "extracted");
                                }
                            }
                        }
                    }
                }
            }

            httpClient.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warning($"Ошибка извлечения фактов: {ex.Message}");
        }
    }

    /// <summary>
    /// Очищает историю диалога, кэш и память.
    /// </summary>
    public void ClearHistory()
    {
        _history.Clear();
        Cache.Clear();
        Memory.Clear();
        Logger.Info("История, кэш и память очищены");
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
        var changes = Memory.GetRecentChanges();
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
