using AIChallenge.Core.Services;
using AIChallenge.Models;
using AIChallenge.Services;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIChallenge.Core.Infrastructure;

/// <summary>
/// Конечный автомат управления задачей.
/// Вызывает GigaChat API на этапах planning/execution/validation,
/// передавая инварианты, профиль агента и системный промт.
/// </summary>
public class TaskStateMachine
{
    private readonly ChatClient? _chatClient;
    private readonly AuthClient? _authClient;
    private readonly GigaChatConfig? _config;
    private readonly AgentProfile? _agentProfile;
    /// <summary>
    /// Профиль агента (для проверки инвариантов).
    /// </summary>
    public AgentProfile? AgentProfile => _agentProfile;

    private TaskState _state;
    /// <summary>
    /// Матрица допустимых переходов между состояниями FSM.
    /// Ключ — текущее состояние, значение — список состояний, в которые можно перейти.
    /// Любая попытка перехода, отсутствующего в этой матрице, считается ошибкой.
    /// </summary>
    public static readonly Dictionary<TaskStage, List<TaskStage>> AllowedTransitions = new()
    {
        [TaskStage.Requirements] = new() { TaskStage.Planning },
        [TaskStage.Planning]    = new() { TaskStage.Execution, TaskStage.Requirements },
        [TaskStage.Execution]   = new() { TaskStage.Validation, TaskStage.Planning },
        [TaskStage.Validation]  = new() { TaskStage.Done, TaskStage.Execution },
        [TaskStage.Done]        = new(),
        [TaskStage.Paused]      = new() { TaskStage.Resuming },
        [TaskStage.Resuming]    = new(),
    };

    private static readonly Dictionary<TaskStage, (int Number, int Total, string Description)> _defaultSteps = new()
    {
        [TaskStage.Requirements] = (1, 1, "Сбор требований"),
        [TaskStage.Planning]     = (1, 1, "Планирование"),
        [TaskStage.Execution]    = (1, 1, "Выполнение"),
        [TaskStage.Validation]   = (1, 1, "Валидация"),
        [TaskStage.Done]         = (1, 1, "Задача завершена"),
    };

    /// <summary>
    /// Создаёт FSM без подключения к LLM (только управление состоянием).
    /// </summary>
    public TaskStateMachine()
    {
        _state = new TaskState(
            Stage: TaskStage.Requirements,
            Step: new TaskStep(1, 1, "Сбор требований"),
            NextAction: "Ответ пользователя на текущий вопрос",
            History: new List<HistoryEntry>(),
            Paused: false,
            RequirementsContext: null,
            PlanApproved: false,
            Artifacts: new List<ArtifactEntry>(),
            ValidationPassed: false,
            ValidationFeedback: null);
    }

    /// <summary>
    /// Создаёт FSM с подключением к GigaChat API.
    /// </summary>
    public TaskStateMachine(ChatClient chatClient, AuthClient authClient, GigaChatConfig config, AgentProfile agentProfile)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _agentProfile = agentProfile ?? throw new ArgumentNullException(nameof(agentProfile));

        _state = new TaskState(
            Stage: TaskStage.Requirements,
            Step: new TaskStep(1, 1, "Сбор требований"),
            NextAction: "Ответ пользователя на текущий вопрос",
            History: new List<HistoryEntry>(),
            Paused: false,
            RequirementsContext: null,
            PlanApproved: false,
            Artifacts: new List<ArtifactEntry>(),
            ValidationPassed: false,
            ValidationFeedback: null);
    }

    private TaskStateMachine(TaskState state)
    {
        _state = state;
    }

    // ── Properties ──────────────────────────────────────────────

    public TaskStage Stage => _state.Stage;

    public TaskStep Step => _state.Step;

    public string NextAction => _state.NextAction;

    public bool Paused => _state.Paused;

    public bool IsDone => _state.Stage == TaskStage.Done;

    public IReadOnlyList<HistoryEntry> History => _state.History;

    public RequirementsContext? RequirementsContext => _state.RequirementsContext;

    public bool PlanApproved => _state.PlanApproved;

    public IReadOnlyList<ArtifactEntry> Artifacts => _state.Artifacts;

    public bool ValidationPassed => _state.ValidationPassed;

    public string? ValidationFeedback => _state.ValidationFeedback;

    // ── Basic methods ───────────────────────────────────────────

    /// <summary>
    /// Проверка допустимости перехода из текущего состояния в targetStage.
    /// Проверяет матрицу переходов и бизнес-правила (предусловия).
    /// </summary>
    public TransitionResult CanTransition(TaskStage targetStage)
    {
        var currentStage = _state.Stage;
        var allowedNext = AllowedTransitions.GetValueOrDefault(currentStage, new List<TaskStage>());

        // Если целевое состояние не в матрице разрешённых переходов — запрещаем
        if (!allowedNext.Contains(targetStage))
        {
            return new TransitionResult(
                Allowed: false,
                Reason: $"Нельзя перейти из '{currentStage}' в '{targetStage}'. Сначала нужно завершить этап '{GetPrecedingStage(currentStage, targetStage)}'.",
                CurrentStage: currentStage.ToString(),
                TargetStage: targetStage.ToString(),
                AllowedNext: allowedNext.Select(s => s.ToString()).ToList(),
                MissingConditions: null);
        }

        // Проверяем бизнес-правила (предусловия) для каждого перехода
        var missingConditions = new List<string>();

        if (currentStage == TaskStage.Requirements && targetStage == TaskStage.Planning)
        {
            if (_state.RequirementsContext is null)
            {
                missingConditions.Add("requirements_context");
            }
            else if (_state.RequirementsContext.CurrentQuestionIndex < _state.RequirementsContext.Questions.Count)
            {
                missingConditions.Add("all_questions_answered");
            }
        }

        if (currentStage == TaskStage.Planning && targetStage == TaskStage.Execution)
        {
            if (!_state.PlanApproved)
                missingConditions.Add("plan_approved");
        }

        if (currentStage == TaskStage.Execution && targetStage == TaskStage.Validation)
        {
            if (_state.Artifacts.Count == 0)
                missingConditions.Add("artifacts_present");
        }

        if (currentStage == TaskStage.Validation && targetStage == TaskStage.Done)
        {
            if (!_state.ValidationPassed)
                missingConditions.Add("validation_passed");
        }

        if (currentStage == TaskStage.Validation && targetStage == TaskStage.Execution)
        {
            if (_state.ValidationPassed)
                missingConditions.Add("validation_failed");
        }

        var conditionsMet = missingConditions.Count == 0;

        string? reason = null;
        if (!conditionsMet)
        {
            var conditionDescriptions = new Dictionary<string, string>
            {
                ["requirements_context"] = "Необходим контекст требований (вопросы заданы и отвечены).",
                ["all_questions_answered"] = $"Не все вопросы отвечены: {_state.RequirementsContext?.CurrentQuestionIndex ?? 0} из {_state.RequirementsContext?.Questions.Count ?? 0}.",
                ["plan_approved"] = "План не утверждён. Вызовите approve_plan().",
                ["artifacts_present"] = "Нет артефактов. Добавьте артефакты через add_artifact().",
                ["validation_passed"] = "Валидация не пройдена. Вызовите set_validation_result(true).",
                ["validation_failed"] = "Валидация пройдена. Для возврата к Execution вызовите set_validation_result(false, feedback).",
            };

            var desc = missingConditions
                .Select(c => conditionDescriptions.GetValueOrDefault(c, c))
                .Aggregate((a, b) => a + " " + b);
            reason = $"Переход запрещён: {desc}";
        }

        return new TransitionResult(
            Allowed: conditionsMet,
            Reason: reason,
            CurrentStage: currentStage.ToString(),
            TargetStage: targetStage.ToString(),
            AllowedNext: allowedNext.Select(s => s.ToString()).ToList(),
            MissingConditions: conditionsMet ? null : missingConditions);
    }

    /// <summary>
    /// Определяет название предыдущего этапа для сообщения об ошибке.
    /// </summary>
    private static string GetPrecedingStage(TaskStage current, TaskStage target)
    {
        // Для перехода из Requirements в Execution — нужно сначала завершить Planning
        return target switch
        {
            TaskStage.Planning => "requirements",
            TaskStage.Execution => "planning",
            TaskStage.Validation => "execution",
            TaskStage.Done => "validation",
            _ => current.ToString()
        };
    }

    /// <summary>
    /// Переход между этапами с жёсткой валидацией.
    /// Перед переходом вызывает CanTransition. Если переход разрешён — выполняет его.
    /// Если переход не разрешён — выбрасывает InvalidTransitionError.
    /// </summary>
    public void Transition(TaskStage nextStage)
    {
        // Проверяем, не на паузе ли задача
        if (_state.Paused)
            throw new InvalidOperationException("Нельзя перейти этап, пока задача на паузе. Вызовите Resume().");

        // Проверяем, не завершена ли задача
        if (_state.Stage == TaskStage.Done)
            throw new InvalidOperationException("Нельзя перейти из этапа Done.");

        // Вызываем CanTransition для проверки
        var result = CanTransition(nextStage);

        if (!result.Allowed)
        {
            throw new InvalidTransitionError(
                currentStage: _state.Stage,
                targetStage: nextStage,
                allowedNext: result.AllowedNext.Select(s => Enum.Parse<TaskStage>(s)).ToList(),
                reason: result.Reason ?? "Переход запрещён бизнес-правилами.");
        }

        // Сохраняем завершённый шаг в историю
        _state.History.Add(new HistoryEntry(
            Stage: _state.Stage.ToString(),
            StepNumber: _state.Step.Number,
            StepDescription: _state.Step.Description,
            Completed: true,
            CompletedAt: DateTime.UtcNow.ToString("o")));

        _state = _state with
        {
            Stage = nextStage,
            Step = MapDefaultStep(nextStage),
            Paused = false,
            NextAction = ComputeNextAction(nextStage)
        };
    }

    /// <summary>
    /// Установить текущий шаг.
    /// </summary>
    public void SetStep(int number, int total, string description)
    {
        if (number < 1) throw new ArgumentOutOfRangeException(nameof(number), "Номер шага должен быть >= 1.");
        if (total < 1) throw new ArgumentOutOfRangeException(nameof(total), "Total должен быть >= 1.");
        if (number > total) throw new ArgumentException("Number не может быть больше Total.");

        _state = _state with { Step = new TaskStep(number, total, description) };
    }

    /// <summary>
    /// Установить ожидаемое действие.
    /// </summary>
    public void SetNextAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action не может быть пустым.", nameof(action));

        _state = _state with { NextAction = action };
    }

    /// <summary>
    /// Поставить задачу на паузу.
    /// </summary>
    public void Pause()
    {
        if (_state.Stage == TaskStage.Done)
            throw new InvalidOperationException("Нельзя поставить на паузу завершённую задачу.");

        _state = _state with { Paused = true };
    }

    /// <summary>
    /// Возобновить задачу с того же шага.
    /// </summary>
    public void Resume()
    {
        if (_state.Stage == TaskStage.Done)
            throw new InvalidOperationException("Нельзя возобновить завершённую задачу.");

        _state = _state with { Paused = false };
    }

    /// <summary>
    /// Проверка завершения.
    /// </summary>
    public bool IsCompleted() => _state.Stage == TaskStage.Done;

    /// <summary>
    /// Сброс состояния при завершении задачи.
    /// </summary>
    public void ResetAfterCompletion()
    {
        _state = new TaskState(
            Stage: TaskStage.Requirements,
            Step: new TaskStep(1, 1, "Сбор требований"),
            NextAction: "Ответ пользователя на текущий вопрос",
            History: new List<HistoryEntry>(),
            Paused: false,
            RequirementsContext: null,
            PlanApproved: false,
            Artifacts: new List<ArtifactEntry>(),
            ValidationPassed: false,
            ValidationFeedback: null);
    }

    /// <summary>
    /// Вернуть текущее состояние.
    /// </summary>
    public TaskState GetState() => _state;

    /// <summary>
    /// Сериализовать состояние в JSON.
    /// </summary>
    public string ToJson()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        return JsonSerializer.Serialize(_state, options);
    }

    /// <summary>
    /// Десериализовать состояние из JSON.
    /// </summary>
    public static TaskStateMachine FromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var state = JsonSerializer.Deserialize<TaskState>(json, options);
        if (state is null)
            throw new JsonException("Не удалось десериализовать состояние.");

        // При десериализации автоматически снимаем с паузы —
        // загрузка состояния подразумевает возобновление работы
        var resumedState = state with { Paused = false };
        return new TaskStateMachine(resumedState);
    }

    // ── Requirements methods ────────────────────────────────────

    /// <summary>
    /// Задать список вопросов для сбора требований.
    /// </summary>
    public void SetQuestions(List<string> questions)
    {
        if (questions is null) throw new ArgumentNullException(nameof(questions));
        if (questions.Count == 0) throw new ArgumentException("Список вопросов не может быть пустым.", nameof(questions));
        if (_state.Stage != TaskStage.Requirements)
            throw new InvalidOperationException("SetQuestions можно вызывать только на этапе Requirements.");

        _state = _state with
        {
            RequirementsContext = new RequirementsContext(
                Questions: new List<string>(questions),
                Answers: new Dictionary<string, string>(),
                CurrentQuestionIndex: 0,
                DialogHistory: new List<DialogEntry>())
        };
    }

    /// <summary>
    /// Вернуть следующий вопрос пользователю (или null, если вопросы закончились).
    /// </summary>
    public string? AskNextQuestion()
    {
        if (_state.RequirementsContext is null)
            throw new InvalidOperationException("Сначала вызовите SetQuestions().");

        var ctx = _state.RequirementsContext;
        if (ctx.CurrentQuestionIndex >= ctx.Questions.Count)
            return null;

        return ctx.Questions[ctx.CurrentQuestionIndex];
    }

    /// <summary>
    /// Записать ответ, сдвинуть current_question_index, добавить в dialog_history.
    /// </summary>
    public void ReceiveAnswer(string answer)
    {
        if (_state.RequirementsContext is null)
            throw new InvalidOperationException("Сначала вызовите SetQuestions().");

        var ctx = _state.RequirementsContext;
        if (ctx.CurrentQuestionIndex >= ctx.Questions.Count)
            throw new InvalidOperationException("Все вопросы уже заданы.");

        var question = ctx.Questions[ctx.CurrentQuestionIndex];

        ctx = _state.RequirementsContext with
        {
            Answers = new Dictionary<string, string>(ctx.Answers)
            {
                [question] = answer
            },
            CurrentQuestionIndex = ctx.CurrentQuestionIndex + 1,
            DialogHistory = new List<DialogEntry>(ctx.DialogHistory)
            {
                new DialogEntry("agent", question),
                new DialogEntry("user", answer)
            }
        };

        _state = _state with { RequirementsContext = ctx };
    }

    /// <summary>
    /// Вернуть собранные ответы (вызывается при переходе в planning).
    /// </summary>
    public Dictionary<string, string> GetRequirementsResult()
    {
        if (_state.RequirementsContext is null)
            throw new InvalidOperationException("Нет контекста требований.");

        return new Dictionary<string, string>(_state.RequirementsContext.Answers);
    }

    // ── Dialog methods ──────────────────────────────────────────

    /// <summary>
    /// Добавить запись в историю диалога текущего этапа.
    /// </summary>
    public void AddDialogEntry(string role, string text)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role не может быть пустым.", nameof(role));

        if (_state.RequirementsContext is not null)
        {
            var ctx = _state.RequirementsContext;
            _state = _state with
            {
                RequirementsContext = ctx with
                {
                    DialogHistory = new List<DialogEntry>(ctx.DialogHistory)
                    {
                        new DialogEntry(role, text)
                    }
                }
            };
        }
        // Для других этапов можно расширить аналогично
    }

    /// <summary>
    /// Вернуть историю диалога текущего этапа.
    /// </summary>
    public IReadOnlyList<DialogEntry> GetDialogHistory()
    {
        if (_state.RequirementsContext is null)
            return Array.Empty<DialogEntry>();

        return _state.RequirementsContext.DialogHistory;
    }

    // ── Business Rule Methods ───────────────────────────────────

    /// <summary>
    /// Утверждает план (ставит plan_approved = True).
    /// Необходимое условие для перехода из Planning в Execution.
    /// </summary>
    public void ApprovePlan()
    {
        if (_state.Stage != TaskStage.Planning)
            throw new InvalidOperationException("Утвердить план можно только на этапе Planning.");

        _state = _state with { PlanApproved = true };
    }

    /// <summary>
    /// Добавляет артефакт, созданный на этапе Execution.
    /// Необходимое условие для перехода из Execution в Validation.
    /// </summary>
    public void AddArtifact(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name не может быть пустым.", nameof(name));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path не может быть пустым.", nameof(path));

        var artifact = new ArtifactEntry(
            Name: name,
            Path: path,
            CreatedAt: DateTime.UtcNow.ToString("o"));

        var newArtifacts = new List<ArtifactEntry>(_state.Artifacts) { artifact };
        _state = _state with { Artifacts = newArtifacts };
    }

    /// <summary>
    /// Устанавливает результат валидации.
    /// passed = true — для перехода Validation → Done.
    /// passed = false — для возврата Validation → Execution (с описанием причины).
    /// </summary>
    public void SetValidationResult(bool passed, string? feedback = null)
    {
        if (_state.Stage != TaskStage.Validation)
            throw new InvalidOperationException("Установить результат валидации можно только на этапе Validation.");

        _state = _state with
        {
            ValidationPassed = passed,
            ValidationFeedback = feedback
        };
    }

    /// <summary>
    /// Возвращает список состояний, в которые можно перейти из текущего,
    /// с учётом и матрицы переходов, и бизнес-правил.
    /// </summary>
    public IReadOnlyList<TransitionOption> GetAllowedTransitions()
    {
        var currentStage = _state.Stage;
        var allowedNext = AllowedTransitions.GetValueOrDefault(currentStage, new List<TaskStage>());

        var options = new List<TransitionOption>();

        foreach (var target in allowedNext)
        {
            var result = CanTransition(target);
            options.Add(new TransitionOption(
                Target: target,
                ConditionsMet: result.Allowed,
                Missing: result.MissingConditions ?? Array.Empty<string>()));
        }

        return options;
    }

    // ── LLM Integration ─────────────────────────────────────────

    /// <summary>
    /// Выполняет этап Planning — формирует план на основе собранных требований.
    /// Передаёт инварианты, профиль агента и системный промт в system message.
    /// </summary>
    public async Task<string> ExecutePlanningAsync()
    {
        EnsureLlmAvailable();

        var requirements = GetRequirementsResult();
        var systemMessage = BuildExtendedSystemMessage("planning", requirements);

        Console.WriteLine($"   [FSM] System message length: {systemMessage.Length} chars");
        Console.WriteLine($"   [FSM] Invariants count: {_agentProfile?.Invariants.Count ?? 0}");
        if (_agentProfile?.Invariants.Count > 0)
        {
            Console.WriteLine($"   [FSM] First invariant: {_agentProfile.Invariants[0][..Math.Min(80, _agentProfile.Invariants[0].Length)]}");
        }

        var messages = new List<ApiMessage>
        {
            new() { Role = "user", Content = BuildPlanningPrompt(requirements) }
        };

        var token = await _authClient!.GetAccessTokenAsync();
        var response = await _chatClient!.SendCompletionAsync(
            _config!.Model,
            messages,
            maxTokens: 4096,
            temperature: 0.3,
            stopSequences: Array.Empty<string>(),
            systemMessage,
            token
        );

        return response.Content;
    }

    /// <summary>
    /// Выполняет шаг плана на этапе Execution.
    /// </summary>
    public async Task<string> ExecuteStepAsync(string stepDescription)
    {
        EnsureLlmAvailable();

        var requirements = GetRequirementsResult();
        var systemMessage = BuildExtendedSystemMessage("execution", requirements);

        var messages = new List<ApiMessage>
        {
            new() { Role = "user", Content = BuildExecutionPrompt(stepDescription) }
        };

        var token = await _authClient!.GetAccessTokenAsync();
        var response = await _chatClient!.SendCompletionAsync(
            _config!.Model,
            messages,
            maxTokens: 4096,
            temperature: 0.5,
            stopSequences: Array.Empty<string>(),
            systemMessage,
            token
        );

        return response.Content;
    }

    /// <summary>
    /// Выполняет валидацию результата.
    /// </summary>
    public async Task<(bool Passed, string Feedback)> ValidateAsync(string result)
    {
        EnsureLlmAvailable();

        var requirements = GetRequirementsResult();
        var systemMessage = BuildExtendedSystemMessage("validation", requirements);

        var messages = new List<ApiMessage>
        {
            new() { Role = "user", Content = BuildValidationPrompt(result) }
        };

        var token = await _authClient!.GetAccessTokenAsync();
        var response = await _chatClient!.SendCompletionAsync(
            _config!.Model,
            messages,
            maxTokens: 2048,
            temperature: 0.1,
            stopSequences: Array.Empty<string>(),
            systemMessage,
            token
        );

        var content = response.Content.ToLowerInvariant();
        var passed = content.Contains("pass") || content.Contains("пройдена") || content.Contains("одобр") || content.Contains("✅") || content.Contains("соответствует");
        var failed = content.Contains("fail") || content.Contains("не соответствует") || content.Contains("неполн");

        // Если явный FAIL — принудительно возвращаем false
        if (failed && !passed)
            passed = false;

        return (passed, response.Content);
    }

    // ── Prompt Builders ─────────────────────────────────────────

    private string BuildPlanningPrompt(Dictionary<string, string> requirements)
    {
        var sb = new StringBuilder();
        sb.AppendLine("На основе собранных требований создай детальный план реализации.");
        sb.AppendLine();
        sb.AppendLine("Требования:");
        foreach (var kvp in requirements)
        {
            sb.AppendLine($"  • {kvp.Key}: {kvp.Value}");
        }
        sb.AppendLine();
        sb.AppendLine("Формат ответа:");
        sb.AppendLine("1. Список подзадач с номерами");
        sb.AppendLine("2. Для каждой подзадачи: описание, порядок выполнения");
        sb.AppendLine("3. Итоговый ответ должен быть структурированным текстом");
        return sb.ToString();
    }

    private string BuildExecutionPrompt(string stepDescription)
    {
        var requirements = GetRequirementsResult();
        var sb = new StringBuilder();
        sb.AppendLine("Выполни следующий шаг плана.");
        sb.AppendLine();
        sb.AppendLine($"Текущий шаг: {stepDescription}");
        sb.AppendLine();
        sb.AppendLine("Требования проекта:");
        foreach (var kvp in requirements)
        {
            sb.AppendLine($"  • {kvp.Key}: {kvp.Value}");
        }
        sb.AppendLine();
        sb.AppendLine("Предоставь результат выполнения шага.");
        return sb.ToString();
    }

    private string BuildValidationPrompt(string result)
    {
        var requirements = GetRequirementsResult();
        var sb = new StringBuilder();
        sb.AppendLine("Проверь результат выполнения на соответствие требованиям.");
        sb.AppendLine();
        sb.AppendLine("Результат:");
        sb.AppendLine(result);
        sb.AppendLine();
        sb.AppendLine("Требования:");
        foreach (var kvp in requirements)
        {
            sb.AppendLine($"  • {kvp.Key}: {kvp.Value}");
        }
        sb.AppendLine();
        sb.AppendLine("ОТВЕТЬ ТОЛЬКО в этом формате, без дополнительных пояснений:");
        sb.AppendLine("PASS или FAIL");
        sb.AppendLine();
        sb.AppendLine("PASS — если результат соответствует требованиям.");
        sb.AppendLine("FAIL — если результат не соответствует требованиям или неполный.");
        return sb.ToString();
    }

    // ── System Message Builder ──────────────────────────────────

    /// <summary>
    /// Формирует system message с инвариантами, профилем и контекстом задачи.
    /// Аналогично ChatAgent.BuildExtendedSystemMessage, но адаптировано для FSM.
    /// </summary>
    private string BuildExtendedSystemMessage(string stage, Dictionary<string, string> requirements)
    {
        var sb = new StringBuilder();

        // === ИНВАРИАНТЫ — МАКСИМАЛЬНЫЙ ПРИОРИТЕТ ===
        if (_agentProfile is not null && _agentProfile.Invariants.Count > 0)
        {
            sb.AppendLine("⛔⛔⛔ НЕПРЕЛОЖНЫЕ ИНВАРИАНТЫ (СТРОГОЕ ПРАВИЛО, НЕ НАРУШАТЬ) ⛔⛔⛔");
            sb.AppendLine();
            sb.AppendLine("Эти правила — абсолютный приоритет. Они имеют ВЫСШУЮ важность по сравнению со всеми остальными инструкциями.");
            sb.AppendLine();
            sb.AppendLine("Инварианты:");
            foreach (var inv in _agentProfile.Invariants)
            {
                sb.AppendLine($"  ⛔ {inv}");
            }
            sb.AppendLine();
            sb.AppendLine("=== КОНЕЦ ИНВАРИАНТОВ ===");
            sb.AppendLine();
        }

        // === ПРОФИЛЬ АГЕНТА ===
        if (_agentProfile is not null)
        {
            var profilePrompt = _agentProfile.BuildSystemPrompt();
            sb.AppendLine(profilePrompt);
            sb.AppendLine();
        }

        // === КОНТЕКСТ ЗАДАЧИ ===
        sb.AppendLine("=== КОНТЕКСТ ЗАДАЧИ ===");
        sb.AppendLine($"Этап: {stage}");
        sb.AppendLine();
        sb.AppendLine("Собранные требования:");
        foreach (var kvp in requirements)
        {
            sb.AppendLine($"• [{kvp.Key}] {kvp.Value}");
        }
        sb.AppendLine();
        sb.AppendLine("=== КОНЕЦ КОНТЕКСТА ===");
        sb.AppendLine();

        // === СИСТЕМНОЕ СООБЩЕНИЕ ===
        if (!string.IsNullOrEmpty(_config?.SystemMessage))
        {
            sb.AppendLine(_config.SystemMessage);
        }

        return sb.ToString();
    }

    // ── Helpers ─────────────────────────────────────────────────

    private void EnsureLlmAvailable()
    {
        if (_chatClient is null || _authClient is null || _config is null)
            throw new InvalidOperationException(
                "FSM не подключён к GigaChat API. Используйте конструктор с параметрами chatClient, authClient, config, agentProfile.");
    }

    /// <summary>
    /// Задаёт следующий вопрос пользователю.
    /// Если вопросы ещё не сгенерированы — генерирует их через LLM.
    /// Возвращает null когда все вопросы заданы.
    /// </summary>
    public async Task<string?> AskNextQuestionInteractiveAsync(string userAnswer)
    {
        EnsureLlmAvailable();

        // Если это первый вызов — инициализируем контекст и генерируем вопросы
        if (RequirementsContext is null)
        {
            _state = _state with
            {
                RequirementsContext = new RequirementsContext(
                    Questions: new List<string>(),
                    Answers: new Dictionary<string, string>(),
                    CurrentQuestionIndex: 0,
                    DialogHistory: new List<DialogEntry>())
            };
        }

        var ctx = _state.RequirementsContext!;

        // Если вопросов ещё нет — генерируем через LLM
        if (ctx.Questions.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("📋 Этап: Сбор требований");
            Console.ResetColor();
            Console.WriteLine($"   Запрос: {userAnswer}");
            Console.WriteLine("   Генерирую вопросы...");

            var questions = await GenerateQuestionsAsync(userAnswer);
            ctx = ctx with { Questions = new List<string>(questions) };
            _state = _state with { RequirementsContext = ctx };

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   Сгенерировано {questions.Count} вопросов:");
            Console.ResetColor();
            for (int i = 0; i < questions.Count; i++)
            {
                Console.WriteLine($"     {i + 1}. {questions[i]}");
            }
            Console.WriteLine();
        }

        // Записываем ответ пользователя (только если уже задавали вопросы)
        if (ctx.CurrentQuestionIndex > 0)
        {
            var prevQuestion = ctx.Questions[ctx.CurrentQuestionIndex - 1];
            ctx = ctx with
            {
                DialogHistory = new List<DialogEntry>(ctx.DialogHistory)
                {
                    new DialogEntry("agent", prevQuestion),
                    new DialogEntry("user", userAnswer)
                }
            };
            _state = _state with { RequirementsContext = ctx };
        }

        // Возвращаем следующий вопрос или null если все заданы
        return AskNextQuestion();
    }

    /// <summary>
    /// Инициализирует сбор требований и задаёт первый вопрос.
    /// Возвращает первый вопрос или null если все вопросы уже заданы.
    /// </summary>
    public async Task<string?> InitializeRequirementsAsync(string userRequest)
    {
        EnsureLlmAvailable();

        // Если это первый вызов — инициализируем контекст и генерируем вопросы
        if (RequirementsContext is null)
        {
            _state = _state with
            {
                RequirementsContext = new RequirementsContext(
                    Questions: new List<string>(),
                    Answers: new Dictionary<string, string>(),
                    CurrentQuestionIndex: 0,
                    DialogHistory: new List<DialogEntry>())
            };
        }

        var ctx = _state.RequirementsContext!;

        // Если вопросов ещё нет — генерируем через LLM
        if (ctx.Questions.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("📋 Этап: Сбор требований");
            Console.ResetColor();
            Console.WriteLine($"   Запрос: {userRequest}");
            Console.WriteLine("   Генерирую вопросы...");

            var questions = await GenerateQuestionsAsync(userRequest);
            ctx = ctx with { Questions = new List<string>(questions) };
            _state = _state with { RequirementsContext = ctx };

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   Сгенерировано {questions.Count} вопросов:");
            Console.ResetColor();
            for (int i = 0; i < questions.Count; i++)
            {
                Console.WriteLine($"     {i + 1}. {questions[i]}");
            }
            Console.WriteLine();
        }

        // Возвращаем первый вопрос
        return AskNextQuestion();
    }

    /// <summary>
    /// Генерирует список вопросов через LLM на основе запроса пользователя.
    /// </summary>
    private async Task<List<string>> GenerateQuestionsAsync(string userRequest)
    {
        var systemMessage = BuildExtendedSystemMessage("requirements", new Dictionary<string, string>());

        var prompt = $"""
            На основе запроса пользователя сформулируй 5-8 конкретных вопросов для сбора требований.
            Вопросы должны помочь понять:
            - Функциональные требования
            - Нефункциональные требования (производительность, безопасность и т.д.)
            - Целевую аудиторию
            - Ограничения и зависимости
            - Приоритеты

            Запрос пользователя:
            {userRequest}

            Ответь ТОЛЬКО списком вопросов, каждый на новой строке, без нумерации.
            """;

        var messages = new List<ApiMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        var token = await _authClient!.GetAccessTokenAsync();
        var response = await _chatClient!.SendCompletionAsync(
            _config!.Model,
            messages,
            maxTokens: 2048,
            temperature: 0.7,
            stopSequences: Array.Empty<string>(),
            systemMessage,
            token
        );

        // Парсим вопросы из ответа
        var lines = response.Content.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => Regex.Replace(l.Trim(), @"^[\-\*\•\s\d\.]+\s*", ""))
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l) && l.Length > 10)
            .ToList();

        return lines;
    }

    /// <summary>
    /// Возвращает статус сбора требований.
    /// </summary>
    public RequirementsStatus? GetRequirementsStatus()
    {
        if (Stage != TaskStage.Requirements)
            return null;

        if (RequirementsContext is null)
            return new RequirementsStatus(0, 0, 0, false);

        return new RequirementsStatus(
            TotalQuestions: RequirementsContext.Questions.Count,
            AnsweredQuestions: RequirementsContext.CurrentQuestionIndex,
            TotalDialogEntries: RequirementsContext.DialogHistory.Count,
            IsComplete: RequirementsContext.CurrentQuestionIndex >= RequirementsContext.Questions.Count
        );
    }

    /// <summary>
    /// Загружает состояние из другого TaskStateMachine.
    /// </summary>
    public void LoadState(TaskState state)
    {
        _state = state with { Paused = false };
    }

    /// <summary>
    /// Автоматический запуск полного цикла задачи.
    /// Если задача завершена — сбрасывает на Requirements.
    /// Задаёт вопросы (если есть), переходит к Planning, выполняет план, валидирует.
    /// </summary>
    public async Task<RunResult> RunAutoAsync(string userRequest)
    {
        // Если задача завершена — сбрасываем на новый цикл
        if (IsCompleted())
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("🔄 Начинаем новый цикл задачи...");
            Console.ResetColor();
            Console.WriteLine();

            _state = new TaskState(
                Stage: TaskStage.Requirements,
                Step: new TaskStep(1, 1, "Сбор требований"),
                NextAction: "Ответ пользователя на текущий вопрос",
                History: new List<HistoryEntry>(),
                Paused: false,
                RequirementsContext: null,
                PlanApproved: false,
                Artifacts: new List<ArtifactEntry>(),
                ValidationPassed: false,
                ValidationFeedback: null);
        }

        // Если на этапе Requirements и нет вопросов — используем запрос пользователя как контекст
        if (Stage == TaskStage.Requirements && RequirementsContext is null)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("📋 Этап: Сбор требований");
            Console.ResetColor();
            Console.WriteLine($"   Запрос: {userRequest}");
            Console.WriteLine();

            // Сохраняем запрос как контекст требований
            _state = _state with
            {
                RequirementsContext = new RequirementsContext(
                    Questions: new List<string>(),
                    Answers: new Dictionary<string, string> { ["Запрос"] = userRequest },
                    CurrentQuestionIndex: 0,
                    DialogHistory: new List<DialogEntry> { new("user", userRequest) })
            };
        }

        // Переходим к Planning
        if (Stage != TaskStage.Planning)
        {
            try
            {
                Transition(TaskStage.Planning);
            }
            catch (Exception ex)
            {
                return new RunResult(false, $"Ошибка перехода к Planning: {ex.Message}");
            }
        }

        // Выполняем планирование
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("📋 Этап: Планирование");
        Console.ResetColor();
        Console.WriteLine("   Создаю план...");

        var planResult = await ExecutePlanningAsync();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✅ План создан:");
        Console.ResetColor();
        Console.WriteLine($"   {planResult}");
        Console.WriteLine();

        // Утверждаем план — необходимо для перехода в Execution
        ApprovePlan();

        // Переходим к Execution
        if (Stage != TaskStage.Execution)
        {
            try
            {
                Transition(TaskStage.Execution);
            }
            catch (Exception ex)
            {
                return new RunResult(false, $"Ошибка перехода к Execution: {ex.Message}");
            }
        }

        // Выполняем шаги плана (в этом примере — один шаг с результатом планирования)
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("⚙ Этап: Выполнение");
        Console.ResetColor();
        Console.WriteLine("   Выполняю план...");

        var execResult = await ExecuteStepAsync(planResult);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✅ Выполнение завершено:");
        Console.ResetColor();
        Console.WriteLine($"   {execResult}");
        Console.WriteLine();

        // Добавляем артефакт — необходимо для перехода в Validation
        AddArtifact("execution_result", "artifact://exec_result");

        // Переходим к Validation
        if (Stage != TaskStage.Validation)
        {
            try
            {
                Transition(TaskStage.Validation);
            }
            catch (Exception ex)
            {
                return new RunResult(false, $"Ошибка перехода к Validation: {ex.Message}");
            }
        }

        // Валидируем результат
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("🔍 Этап: Валидация");
        Console.ResetColor();
        Console.WriteLine("   Проверяю результат...");

        var (passed, feedback) = await ValidateAsync(execResult);

        // Устанавливаем результат валидации — необходимо для перехода в Done или Execution
        SetValidationResult(passed, passed ? null : feedback);

        Console.ForegroundColor = passed ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"Результат: {(passed ? "PASS ✅" : "FAIL ❌")}");
        Console.ResetColor();
        Console.WriteLine($"   {feedback}");
        Console.WriteLine();

        // Если валидация пройдена — завершаем задачу
        if (passed)
        {
            try
            {
                Transition(TaskStage.Done);
            }
            catch (Exception ex)
            {
                return new RunResult(false, $"Ошибка перехода к Done: {ex.Message}");
            }
        }
        else
        {
            // Если валидация не пройдена — возвращаемся к Execution
            try
            {
                Transition(TaskStage.Execution);
                return new RunResult(false, "Валидация не пройдена. Возвращаюсь к выполнению.");
            }
            catch (Exception ex)
            {
                return new RunResult(false, $"Ошибка возврата к Execution: {ex.Message}");
            }
        }

        return new RunResult(true, "Задача успешно завершена.");
    }

    private TaskStep MapDefaultStep(TaskStage stage)
    {
        return _defaultSteps.TryGetValue(stage, out var d)
            ? new TaskStep(d.Number, d.Total, d.Description)
            : new TaskStep(d.Number, d.Total, d.Description);
    }

    private string ComputeNextAction(TaskStage stage)
    {
        return stage switch
        {
            TaskStage.Requirements => "Ответ пользователя на текущий вопрос",
            TaskStage.Planning     => "Утверждение плана",
            TaskStage.Execution    => "Выполнение текущего шага плана",
            TaskStage.Validation   => "Результат проверки качества",
            TaskStage.Done         => "Задача завершена",
            TaskStage.Paused       => "Задача приостановлена",
            TaskStage.Resuming     => "Задача возобновляется",
            _                      => "Неизвестное действие"
        };
    }

    /// <summary>
    /// Результат автоматического запуска.
    /// </summary>
    public record RunResult(bool IsSuccess, string Message);

    /// <summary>
    /// Статус сбора требований.
    /// </summary>
    public record RequirementsStatus(
        int TotalQuestions,
        int AnsweredQuestions,
        int TotalDialogEntries,
        bool IsComplete);

    /// <summary>
    /// Опция перехода, возвращаемая GetAllowedTransitions.
    /// </summary>
    public record TransitionOption(
        TaskStage Target,
        bool ConditionsMet,
        IReadOnlyList<string> Missing);
}
