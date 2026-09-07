using GigaChatApp.Infrastructure;
using GigaChatApp.Models;

namespace GigaChatApp.Services;

/// <summary>
/// Планировщик — разбивает сложные запросы на подзадачи, выполняет их и комбинирует результаты.
/// </summary>
public class Planner
{
    private readonly ChatClient _httpClient;
    private readonly AuthClient _authClient;
    private string _model;
    private readonly AgentLogger _logger;
    private readonly Memory _memory;
    private int _maxTasks;

    /// <summary>
    /// Максимальное количество подзадач в плане.
    /// </summary>
    public int MaxTasks
    {
        get => _maxTasks;
        set => _maxTasks = Math.Max(1, Math.Min(20, value));
    }

    /// <summary>
    /// Минимальная длина запроса для планирования.
    /// Короткие запросы обрабатываются без декомпозиции.
    /// </summary>
    public int MinComplexityLength { get; set; } = 30;

    /// <summary>
    /// Обновляет модель в планировщике.
    /// </summary>
    public void UpdateModel(string model)
    {
        _model = model;
    }

    /// <summary>
    /// Ключевые слова, указывающие на сложность запроса.
    /// </summary>
    private static readonly string[] ComplexityKeywords = {
        "анализируй", "проанализируй", "сравни", "сравни", "перечисли", "составь",
        "создай", "напиши", "разработай", "спроектируй", "изучи", "проанализируй",
        "каждый", "все", "какие", "почему", "как", "объясни", "объясни",
        "plan", "analyze", "compare", "list", "create", "design", "explain",
        "разбей", "декомпозируй", "пошагово", "по шагам", "последовательно"
    };

    public Planner(
        ChatClient httpClient,
        AuthClient authClient,
        string model,
        AgentLogger logger,
        Memory memory,
        int maxTasks = 5)
    {
        _httpClient = httpClient;
        _authClient = authClient;
        _model = model;
        _logger = logger;
        _memory = memory;
        _maxTasks = maxTasks;
    }

    /// <summary>
    /// Определяет, требует ли запрос декомпозиции.
    /// </summary>
    public bool NeedsDecomposition(string request)
    {
        if (request.Length < MinComplexityLength)
            return false;

        var lower = request.ToLowerInvariant();
        return ComplexityKeywords.Any(kw => lower.Contains(kw));
    }

    /// <summary>
    /// Создаёт план выполнения на основе запроса.
    /// </summary>
    public async Task<Plan?> CreatePlanAsync(string request, List<Fact>? relevantFacts = null)
    {
        try
        {
            var token = await _authClient.GetAccessTokenAsync();

            var factsContext = relevantFacts is { Count: > 0 }
                ? "\n=== КОНТЕКСТ ИЗ ПАМЯТИ ===\n" + string.Join("\n", relevantFacts.Select(f => $"- {f.Key}: {f.Value}"))
                : "";

            var prompt = $"""
                Ты — планировщик задач. Твоя цель — разбить сложный запрос пользователя на подзадачи.

                Запрос пользователя:
                "{request}"
                {factsContext}

                Требования:
                1. Разбей запрос на 2-5 подзадач
                2. Каждая подзадача должна быть конкретной и выполнимой
                3. Подзадачи должны идти в логическом порядке
                4. Не создавай подзадач если запрос простой
                5. Формат ответа строго:
                   PLAN_START
                   1. <описание подзадачи>
                   2. <описание подзадачи>
                   ...
                   PLAN_END

                Если запрос простой и не требует разбивки — напиши:
                   NO_PLAN

                Пример:
                Запрос: "Проанализируй логи веб-сервера и найди ошибки, затем сравни их с логами базы данных"
                Ответ:
                   PLAN_START
                   1. Прочитать и проанализировать логи веб-сервера
                   2. Извлечь и классифицировать ошибки из логов веб-сервера
                   3. Прочитать и проанализировать логи базы данных
                   4. Сравнить ошибки между двумя источниками
                   5. Сформировать итоговый отчёт
                   PLAN_END
                """;

            var messages = new List<Models.ApiMessage>
            {
                new() { Role = "user", Content = prompt },
            };

            var requestObj = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["messages"] = messages.Select(m => new { m.Role, m.Content }).ToList<object>(),
                ["stream"] = false,
                ["temperature"] = 0.1,
            };

            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            client.BaseAddress = new Uri("https://api.giga.chat");

            var response = await client.PostAsJsonAsync("/v1/chat/completions", requestObj);
            client.Dispose();

            if (!response.IsSuccessStatusCode)
                return null;

            var parsed = await response.Content.ReadFromJsonAsync<PlanResponse>();
            if (parsed?.Choices?.Count == 0)
                return null;

            var planText = parsed.Choices[0].Message?.Content ?? "";

            // Парсим план
            return ParsePlan(planText, request);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Ошибка создания плана: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Выполняет все подзадачи плана последовательно.
    /// </summary>
    public async Task<Plan> ExecutePlanAsync(Plan plan, List<Fact>? relevantFacts = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var task in plan.Tasks)
        {
            task.Status = Models.TaskStatus.Running;
            task.StartedAt = DateTime.UtcNow;

            _logger.Info($"Подзадача {task.Id}: {task.Description}");

            try
            {
                var result = await ExecuteTaskAsync(task, relevantFacts);
                task.Result = result;
                task.Status = Models.TaskStatus.Completed;
                task.CompletedAt = DateTime.UtcNow;

                _logger.Info($"Подзадача {task.Id} выполнена ({task.Duration?.TotalMilliseconds:F0} мс)");
            }
            catch (Exception ex)
            {
                task.Status = Models.TaskStatus.Failed;
                task.CompletedAt = DateTime.UtcNow;
                task.Error = ex.Message;

                _logger.Error($"Подзадача {task.Id} не выполнена: {ex.Message}");
            }
        }

        plan.TotalDuration = stopwatch.Elapsed;

        // Комбинируем результаты
        plan.FinalAnswer = await CombineResultsAsync(plan, relevantFacts);

        return plan;
    }

    /// <summary>
    /// Выполняет одну подзадачу.
    /// </summary>
    private async Task<string> ExecuteTaskAsync(TaskItem task, List<Fact>? relevantFacts = null)
    {
        var token = await _authClient.GetAccessTokenAsync();

        var factsContext = relevantFacts is { Count: > 0 }
            ? "\n=== КОНТЕКСТ ИЗ ПАМЯТИ ===\n" + string.Join("\n", relevantFacts.Select(f => $"- {f.Key}: {f.Value}"))
            : "";

        var prompt = $"""
            Выполни подзадачу на основе контекста и результата предыдущих подзадач.

            Подзадача: {task.Description}
            Контекст: {task.Context}
            {factsContext}

            Предыдущие результаты:
            {GetPreviousResults(task.Id)}

            Дай краткий и точный ответ.
            """;

        var messages = new List<Models.ApiMessage>
        {
            new() { Role = "user", Content = prompt },
        };

        var requestObj = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages.Select(m => new { m.Role, m.Content }).ToList<object>(),
            ["stream"] = false,
        };

        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.BaseAddress = new Uri("https://api.giga.chat");

        var response = await client.PostAsJsonAsync("/v1/chat/completions", requestObj);
        client.Dispose();

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Ошибка API: {errorContent}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<TaskResponse>();
        return parsed?.Choices?[0].Message?.Content ?? "Нет результата";
    }

    /// <summary>
    /// Получает результаты предыдущих подзадач.
    /// </summary>
    private string GetPreviousResults(int currentTaskId)
    {
        var results = new StringBuilder();
        foreach (var task in _plansHistory.Values.Where(p => p.Tasks.Any(t => t.Id < currentTaskId && t.Status == Models.TaskStatus.Completed)))
        {
            foreach (var completedTask in task.Tasks.Where(t => t.Id < currentTaskId && t.Status == Models.TaskStatus.Completed))
            {
                results.AppendLine($"  Подзадача {completedTask.Id}: {completedTask.Result}");
            }
        }
        return results.Length > 0 ? results.ToString() : "  (нет предыдущих результатов)";
    }

    /// <summary>
    /// Комбинирует результаты всех подзадач в финальный ответ.
    /// </summary>
    private async Task<string> CombineResultsAsync(Plan plan, List<Fact>? relevantFacts = null)
    {
        var completedTasks = plan.Tasks.Where(t => t.Status == Models.TaskStatus.Completed).ToList();
        var failedTasks = plan.Tasks.Where(t => t.Status == Models.TaskStatus.Failed).ToList();

        if (completedTasks.Count == 0)
        {
            return "Не удалось выполнить ни одну подзадачу. Попробуйте переформулировать запрос.";
        }

        var token = await _authClient.GetAccessTokenAsync();

        var resultsText = string.Join("\n\n", completedTasks.Select(t =>
            $"Подзадача {t.Id}: {t.Result}"));

        var failedText = failedTasks.Count > 0
            ? $"\n\n⚠️ Не удалось выполнить: {string.Join(", ", failedTasks.Select(t => t.Description))}"
            : "";

        var factsContext = relevantFacts is { Count: > 0 }
            ? "\n=== КОНТЕКСТ ИЗ ПАМЯТИ ===\n" + string.Join("\n", relevantFacts.Select(f => $"- {f.Key}: {f.Value}"))
            : "";

        var prompt = $"""
            Объедини результаты подзадач в единый связный ответ.

            Исходный запрос: "{plan.OriginalRequest}"
            {failedText}
            {factsContext}

            Результаты подзадач:
            {resultsText}

            Дай полный и структурированный ответ на исходный запрос.
            """;

        var messages = new List<Models.ApiMessage>
        {
            new() { Role = "user", Content = prompt },
        };

        var requestObj = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages.Select(m => new { m.Role, m.Content }).ToList<object>(),
            ["stream"] = false,
        };

        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.BaseAddress = new Uri("https://api.giga.chat");

        var response = await client.PostAsJsonAsync("/v1/chat/completions", requestObj);
        client.Dispose();

        if (!response.IsSuccessStatusCode)
        {
            _logger.Warning("Ошибка комбинирования результатов");
            return completedTasks.Count == 1
                ? completedTasks[0].Result
                : string.Join("\n\n", completedTasks.Select(t => $"[{t.Id}] {t.Result}"));
        }

        var parsed = await response.Content.ReadFromJsonAsync<CombineResponse>();
        return parsed?.Choices?[0].Message?.Content ?? "Ошибка комбинирования";
    }

    /// <summary>
    /// Парсит текст плана в объект Plan.
    /// </summary>
    private static Plan ParsePlan(string planText, string originalRequest)
    {
        var plan = new Plan { OriginalRequest = originalRequest };

        var startIdx = planText.IndexOf("PLAN_START");
        var endIdx = planText.IndexOf("PLAN_END");

        if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx)
            return null;

        var tasksText = planText[(startIdx + "PLAN_START".Length)..endIdx].Trim();
        var lines = tasksText.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        var id = 0;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Пропускаем пустые строки и маркеры
            if (string.IsNullOrEmpty(trimmed) || trimmed == "PLAN_START" || trimmed == "PLAN_END")
                continue;

            // Извлекаем номер и описание
            var colonIdx = trimmed.IndexOf(':');
            var description = colonIdx > 0 ? trimmed[(colonIdx + 1)..].Trim() : trimmed;

            // Извлекаем номер — только если есть разделитель ':'
            if (colonIdx > 0)
            {
                var numStr = trimmed[..colonIdx].Trim();
                if (int.TryParse(numStr.Split('.')[0].Trim(), out var num))
                {
                    id = num;
                }
                else
                {
                    id++;
                }
            }
            else
            {
                id++;
            }

            plan.Tasks.Add(new TaskItem
            {
                Id = id,
                Description = description,
                Context = originalRequest,
            });
        }

        return plan.Tasks.Count > 0 ? plan : null;
    }

    /// <summary>
    /// История планов для доступа к предыдущим результатам.
    /// </summary>
    private readonly Dictionary<string, Plan> _plansHistory = new();
}
