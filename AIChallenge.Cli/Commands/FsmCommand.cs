using AIChallenge.Core.Infrastructure;
using AIChallenge.Models;

namespace AIChallenge.Cli.Commands;

public sealed class FsmCommand : CommandHandler
{
    public override string Name => "fsm";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            await ShowStatusAsync(ctx);
            return true;
        }

        var fsm = ctx.TaskStateMachine;
        var fsmCmd = parts[1].ToLowerInvariant();
        switch (fsmCmd)
        {
            case "status":
            case "":
                await ShowStatusAsync(ctx);
                break;

            case "questions":
                if (parts.Length < 3) { PrintRed("❌ Формат: /fsm questions <вопрос1;вопрос2;вопрос3>"); Console.WriteLine(); }
                else if (fsm.Stage != TaskStage.Requirements) { PrintRed($"❌ SetQuestions можно вызывать только на этапе Requirements (сейчас: {fsm.Stage})"); Console.WriteLine(); }
                else { await RunAsync(() => ExecuteQuestions(fsm, parts)); }
                break;

            case "answer":
                if (parts.Length < 3) { PrintRed("❌ Формат: /fsm answer <ответ>"); Console.WriteLine(); }
                else { await RunAsync(() => ExecuteAnswer(fsm, parts)); }
                break;

            case "next":
                await RunAsync(() => ExecuteNext(fsm));
                break;

            case "plan":
                await RunAsync(() => ExecutePlan(fsm));
                break;

            case "execute":
                if (parts.Length < 3) { PrintRed("❌ Формат: /fsm execute <описание шага>"); Console.WriteLine(); }
                else { await RunAsync(() => ExecuteStep(fsm, parts)); }
                break;

            case "validate":
                if (parts.Length < 3) { PrintRed("❌ Формат: /fsm validate <результат>"); Console.WriteLine(); }
                else { await RunAsync(() => ExecuteValidate(fsm, parts)); }
                break;

            case "transition":
                if (parts.Length < 3) { PrintRed("❌ Формат: /fsm transition <Planning|Execution|Validation|Done>"); Console.WriteLine(); }
                else { await RunAsync(() => ExecuteTransition(fsm, parts[2])); }
                break;

            case "allowed":
                await RunAsync(() => ExecuteAllowed(fsm));
                break;

            case "can":
                if (parts.Length < 3) { PrintRed("❌ Формат: /fsm can <Planning|Execution|Validation|Done>"); Console.WriteLine(); }
                else { await RunAsync(() => ExecuteCan(fsm, parts[2])); }
                break;

            case "stages":
                await RunAsync(() => ExecuteStages(fsm));
                break;

            case "pause":
                await RunAsync(() => ExecutePause(fsm));
                break;

            case "resume":
                await RunAsync(() => ExecuteResume(fsm));
                break;

            case "reset":
                await RunAsync(() => ExecuteReset(fsm));
                break;

            case "save":
                if (parts.Length < 3) { PrintRed("❌ Формат: /fsm save <файл.json>"); Console.WriteLine(); }
                else { await RunAsync(() => ExecuteSave(fsm, parts[2])); }
                break;

            case "load":
                if (parts.Length < 3) { PrintRed("❌ Формат: /fsm load <файл.json>"); Console.WriteLine(); }
                else { await RunAsync(() => ExecuteLoad(fsm, parts[2])); }
                break;

            case "history":
                await RunAsync(() => ExecuteHistory(fsm));
                break;

            case "dialog":
                await RunAsync(() => ExecuteDialog(fsm));
                break;

            case "help":
                await HelpCommand(ctx);
                break;

            default:
                PrintRed($"❌ Неизвестная команда FSM: {fsmCmd}. Введите /fsm help для подсказки.");
                Console.WriteLine();
                break;
        }
        return true;
    }

    /// <summary>
    /// Выполняет команду с общим try/catch и печатью ошибки.
    /// </summary>
    private static async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { PrintRed($"❌ Ошибка: {ex.Message}"); Console.WriteLine(); }
    }

    private static async Task ShowStatusAsync(CommandContext ctx)
    {
        PrintYellow("📋 TaskStateMachine:");
        var fsm = ctx.TaskStateMachine;
        Console.WriteLine($"   Этап: {fsm.Stage}");
        Console.WriteLine($"   Шаг: {fsm.Step.Number}/{fsm.Step.Total} — {fsm.Step.Description}");
        Console.WriteLine($"   Действие: {fsm.NextAction}");
        Console.WriteLine($"   Пауза: {(fsm.Paused ? "да" : "нет")}");
        Console.WriteLine($"   Завершено: {(fsm.IsCompleted() ? "да" : "нет")}");

        var reqStatus = fsm.GetRequirementsStatus();
        if (reqStatus is not null)
        {
            Console.WriteLine();
            PrintCyan("   📝 Сбор требований:");
            Console.WriteLine($"      Вопросов: {reqStatus.TotalQuestions}, Задано: {reqStatus.AnsweredQuestions}");
            if (!reqStatus.IsComplete)
            {
                if (fsm.RequirementsContext is not null)
                {
                    var nextQ = fsm.AskNextQuestion();
                    if (nextQ is not null)
                        Console.WriteLine($"      Следующий вопрос: {nextQ}");
                }
                else
                {
                    Console.WriteLine("      Вопросы ещё не заданы. Используйте /fsm questions <вопрос1;вопрос2>");
                }
            }
            else
            {
                Console.WriteLine("      ✅ Все вопросы заданы");
            }
        }
        Console.WriteLine();
    }

    private static async Task ExecuteQuestions(TaskStateMachine fsm, string[] parts)
    {
        var questionsText = string.Join(" ", parts.Skip(2));
        var questions = questionsText.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (questions.Length == 0)
        {
            PrintRed("❌ Список вопросов не может быть пустым");
            Console.WriteLine();
            return;
        }
        fsm.SetQuestions(new List<string>(questions));
        PrintGreen($"✅ Задано {questions.Length} вопрос(ов):");
        for (int i = 0; i < questions.Length; i++)
            Console.WriteLine($"   {i + 1}. {questions[i]}");
        Console.WriteLine();
    }

    private static async Task ExecuteAnswer(TaskStateMachine fsm, string[] parts)
    {
        var answer = string.Join(" ", parts.Skip(2));
        fsm.ReceiveAnswer(answer);
        PrintGreen($"✅ Ответ записан: \"{answer}\"");
        var nextQuestion = fsm.AskNextQuestion();
        if (nextQuestion is null)
            Console.WriteLine("   Все вопросы заданы! Используйте /fsm transition Planning для перехода к планированию.");
        else
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"   Следующий вопрос:");
            Console.ResetColor();
            Console.WriteLine($"   {nextQuestion}");
        }
        Console.WriteLine();
    }

    private static async Task ExecuteNext(TaskStateMachine fsm)
    {
        if (fsm.RequirementsContext is null)
        {
            PrintYellow("   Вопросы ещё не заданы. Используйте /fsm questions <вопрос1;вопрос2>");
            Console.WriteLine();
            return;
        }
        var nextQ = fsm.AskNextQuestion();
        if (nextQ is null)
        {
            PrintYellow("   Все вопросы заданы.");
            Console.WriteLine();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"   Вопрос: {nextQ}");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    private static async Task ExecutePlan(TaskStateMachine fsm)
    {
        PrintYellow("📋 Выполняю планирование через LLM...");
        var planResult = await fsm.ExecutePlanningAsync();
        PrintGreen("✅ Планирование завершено:");
        Console.WriteLine($"   {planResult}");
        Console.WriteLine();
    }

    private static async Task ExecuteStep(TaskStateMachine fsm, string[] parts)
    {
        var stepDesc = string.Join(" ", parts.Skip(2));
        PrintYellow($"⚙ Выполняю шаг: {stepDesc}");
        var execResult = await fsm.ExecuteStepAsync(stepDesc);
        PrintGreen("✅ Шаг выполнен:");
        Console.WriteLine($"   {execResult}");
        Console.WriteLine();
    }

    private static async Task ExecuteValidate(TaskStateMachine fsm, string[] parts)
    {
        var validationResult = string.Join(" ", parts.Skip(2));
        PrintYellow("🔍 Выполняю валидацию через LLM...");
        var (passed, feedback) = await fsm.ValidateAsync(validationResult);
        Console.ForegroundColor = passed ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"Результат: {(passed ? "PASS ✅" : "FAIL ❌")}");
        Console.ResetColor();
        Console.WriteLine($"   {feedback}");
        Console.WriteLine();
    }

    private static async Task ExecuteTransition(TaskStateMachine fsm, string targetStageStr)
    {
        if (Enum.TryParse<TaskStage>(targetStageStr, ignoreCase: true, out var targetStage))
        {
            var check = fsm.CanTransition(targetStage);
            Console.WriteLine($"🔍 Проверка перехода: {fsm.Stage} → {targetStage}");
            Console.WriteLine($"   Разрешено: {(check.Allowed ? "✅ ДА" : "❌ НЕТ")}");
            if (check.Allowed)
                Console.WriteLine();
            else
            {
                PrintYellow($"   Причина: {check.Reason}");
                Console.WriteLine($"   Разрешённые переходы: [{string.Join(", ", check.AllowedNext)}]");
                if (check.MissingConditions?.Count > 0)
                    Console.WriteLine($"   Не выполнены: [{string.Join(", ", check.MissingConditions)}]");
                Console.ResetColor();
                Console.WriteLine();
            }

            fsm.Transition(targetStage);
            PrintGreen($"✅ Переход выполнен: {targetStage}");
            Console.WriteLine($"   Шаг: {fsm.Step.Number}/{fsm.Step.Total} — {fsm.Step.Description}");
            Console.WriteLine($"   Действие: {fsm.NextAction}");
            Console.WriteLine();
        }
        else
        {
            PrintRed($"❌ Неизвестный этап: '{targetStageStr}'. Доступны: Requirements, Planning, Execution, Validation, Done");
            Console.WriteLine();
        }
    }

    private static async Task ExecuteAllowed(TaskStateMachine fsm)
    {
        var allowed = fsm.GetAllowedTransitions();
        Console.WriteLine($"📋 Допустимые переходы из '{fsm.Stage}':");
        Console.WriteLine();
        if (allowed.Count == 0)
        {
            Console.WriteLine("   Нет доступных переходов (задача завершена или нет допустимых переходов).");
        }
        else
        {
            foreach (var opt in allowed)
            {
                var icon = opt.ConditionsMet ? "✅" : "❌";
                Console.ForegroundColor = opt.ConditionsMet ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"   {icon} {opt.Target}");
                Console.ResetColor();
                if (!opt.ConditionsMet && opt.Missing?.Count > 0)
                    Console.WriteLine($"       Не выполнены: [{string.Join(", ", opt.Missing)}]");
            }
        }
        Console.WriteLine();
    }

    private static async Task ExecuteCan(TaskStateMachine fsm, string canStageStr)
    {
        if (Enum.TryParse<TaskStage>(canStageStr, ignoreCase: true, out var canStage))
        {
            var checkResult = fsm.CanTransition(canStage);
            Console.WriteLine($"🔍 Можно ли перейти из '{checkResult.CurrentStage}' в '{checkResult.TargetStage}'?");
            Console.WriteLine($"   Ответ: {(checkResult.Allowed ? "✅ ДА" : "❌ НЕТ")}");
            if (!checkResult.Allowed)
            {
                PrintYellow($"   Причина: {checkResult.Reason}");
                Console.ResetColor();
            }
            Console.WriteLine();
        }
        else
        {
            PrintRed($"❌ Неизвестный этап: '{canStageStr}'. Доступны: Requirements, Planning, Execution, Validation, Done");
            Console.WriteLine();
        }
    }

    private static async Task ExecuteStages(TaskStateMachine fsm)
    {
        Console.WriteLine("📋 Полный список этапов задачи:");
        Console.WriteLine();

        var orderedStages = new[] {
            TaskStage.Requirements, TaskStage.Planning, TaskStage.Execution,
            TaskStage.Validation, TaskStage.Done, TaskStage.Paused, TaskStage.Resuming
        };

        var stageDescriptions = new Dictionary<TaskStage, string>
        {
            [TaskStage.Requirements] = "Сбор требований",
            [TaskStage.Planning] = "Планирование",
            [TaskStage.Execution] = "Выполнение",
            [TaskStage.Validation] = "Валидация",
            [TaskStage.Done] = "Задача завершена",
            [TaskStage.Paused] = "Пауза (служебное)",
            [TaskStage.Resuming] = "Возобновление (служебное)"
        };

        var currentStage = fsm.Stage;
        var isCurrent = (TaskStage s) => s == currentStage ? " ◀ текущий" : "";

        Console.WriteLine("  Этап                      | Описание");
        Console.WriteLine("  ──────────────────────────┼───────────────────────────────");
        foreach (var stage in orderedStages)
        {
            var name = stage.ToString().PadRight(25);
            var desc = stageDescriptions.GetValueOrDefault(stage, "");
            var marker = isCurrent(stage);
            Console.WriteLine($"  {name} │ {desc}{marker}");
        }

        Console.WriteLine();
        Console.WriteLine("🔗 Допустимые переходы:");
        Console.WriteLine();

        foreach (var stage in orderedStages)
        {
            var allowed = TaskStateMachine.AllowedTransitions.GetValueOrDefault(stage, new List<TaskStage>());
            if (allowed.Count > 0)
            {
                var arrow = allowed.Count == 1
                    ? $"→ {allowed[0]}"
                    : $"→ {string.Join(", ", allowed)}";
                Console.WriteLine($"  {stage,-20} {arrow}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("⚠️  Этап Done не имеет допустимых переходов — задача завершена.");
        Console.WriteLine();
    }

    private static async Task ExecuteResume(TaskStateMachine fsm)
    {
        var resumeFile = Path.Combine(AppContext.BaseDirectory, "fsm_state.json");
        if (File.Exists(resumeFile))
        {
            var json = File.ReadAllText(resumeFile, Encoding.UTF8);
            var loaded = TaskStateMachine.FromJson(json);
            fsm.LoadState(loaded.GetState());

            PrintGreen($"▶ Задача возобновлена из: {resumeFile}");
            Console.WriteLine($"   Этап: {fsm.Stage}");
            Console.WriteLine($"   Шаг: {fsm.Step.Number}/{fsm.Step.Total} — {fsm.Step.Description}");

            var reqStatusLoad = fsm.GetRequirementsStatus();
            if (reqStatusLoad is not null)
            {
                Console.WriteLine($"   Вопросы: {reqStatusLoad.AnsweredQuestions}/{reqStatusLoad.TotalQuestions} задано");
                if (!reqStatusLoad.IsComplete)
                {
                    if (fsm.RequirementsContext is not null)
                    {
                        var nextQ = fsm.AskNextQuestion();
                        if (nextQ is not null)
                            Console.WriteLine($"   Следующий вопрос: {nextQ}");
                    }
                }
            }
            Console.WriteLine();
        }
        else
        {
            fsm.Resume();
            PrintGreen("▶ Задача возобновлена");
            Console.WriteLine();
        }
    }

    private static async Task ExecuteReset(TaskStateMachine fsm)
    {
        fsm.ResetAfterCompletion();
        var resetFile = "fsm_state.json";
        if (File.Exists(resetFile))
        {
            File.Delete(resetFile);
            PrintYellow($"🗑 Файл состояния удалён: {resetFile}");
        }
        PrintGreen("✅ Состояние FSM сброшено");
        Console.WriteLine($"   Этап: {fsm.Stage}");
        Console.WriteLine($"   Шаг: {fsm.Step.Number}/{fsm.Step.Total} — {fsm.Step.Description}");
        Console.WriteLine();
    }

    private static async Task ExecuteSave(TaskStateMachine fsm, string saveFile)
    {
        var json = fsm.ToJson();
        File.WriteAllText(saveFile, json, Encoding.UTF8);
        PrintGreen($"✅ Состояние сохранено в {saveFile}");
        Console.WriteLine();
    }

    private static async Task ExecuteLoad(TaskStateMachine fsm, string loadFile)
    {
        if (!File.Exists(loadFile))
        {
            PrintRed($"❌ Файл не найден: {loadFile}");
            Console.WriteLine();
            return;
        }
        var json = File.ReadAllText(loadFile, Encoding.UTF8);
        var loaded = TaskStateMachine.FromJson(json);
        fsm.LoadState(loaded.GetState());

        PrintGreen($"✅ Состояние загружено из {loadFile}");
        Console.WriteLine($"   Этап: {fsm.Stage}");
        Console.WriteLine($"   Шаг: {fsm.Step.Number}/{fsm.Step.Total} — {fsm.Step.Description}");

        var reqStatusLoad = fsm.GetRequirementsStatus();
        if (reqStatusLoad is not null)
        {
            Console.WriteLine($"   Вопросы: {reqStatusLoad.AnsweredQuestions}/{reqStatusLoad.TotalQuestions} задано");
            if (!reqStatusLoad.IsComplete)
            {
                if (fsm.RequirementsContext is not null)
                {
                    var nextQ = fsm.AskNextQuestion();
                    Console.WriteLine($"   Следующий вопрос: {nextQ ?? "(все заданы)"}");
                }
                else
                {
                    Console.WriteLine("   Вопросы ещё не заданы.");
                }
            }
        }
        Console.WriteLine();
    }

    private static async Task ExecuteHistory(TaskStateMachine fsm)
    {
        PrintYellow("📜 История переходов:");
        var history = fsm.History;
        if (history.Count == 0)
        {
            Console.WriteLine("   (пусто)");
        }
        else
        {
            foreach (var h in history)
            {
                Console.WriteLine($"   [{h.Stage}] шаг #{h.StepNumber}: {h.StepDescription} (завершено: {h.CompletedAt})");
            }
        }
        Console.WriteLine();
    }

    private static async Task ExecuteDialog(TaskStateMachine fsm)
    {
        PrintYellow("💬 История диалога текущего этапа:");
        var dialogHistory = fsm.GetDialogHistory();
        if (dialogHistory.Count == 0)
        {
            Console.WriteLine("   (пусто)");
        }
        else
        {
            foreach (var d in dialogHistory)
            {
                var color = d.Role switch
                {
                    "user" => ConsoleColor.Green,
                    "agent" => ConsoleColor.Cyan,
                    _ => ConsoleColor.White,
                };
                Console.ForegroundColor = color;
                Console.Write($"   [{d.Role,-8}] ");
                Console.ResetColor();
                Console.WriteLine(d.Text);
            }
        }
        Console.WriteLine();
    }

    private static async Task HelpCommand(CommandContext ctx)
    {
        Console.WriteLine("📖 Справка по FSM-командам:");
        Console.WriteLine();
        Console.WriteLine("   /fsm status — текущий статус");
        Console.WriteLine("   /fsm stages — полный список этапов и переходов");
        Console.WriteLine("   /fsm questions <вопрос1;вопрос2> — задать вопросы вручную");
        Console.WriteLine("   /fsm answer <ответ> — ответить на текущий вопрос");
        Console.WriteLine("   /fsm next — показать следующий вопрос");
        Console.WriteLine("   /fsm plan — выполнить планирование через LLM");
        Console.WriteLine("   /fsm execute <шаг> — выполнить шаг через LLM");
        Console.WriteLine("   /fsm validate <результат> — валидация через LLM");
        Console.WriteLine("   /fsm transition <этап> — перейти в этап (с проверкой)");
        Console.WriteLine("   /fsm can <этап> — проверить, можно ли перейти (без перехода)");
        Console.WriteLine("   /fsm allowed — показать все допустимые переходы");
        Console.WriteLine("   /fsm pause — пауза (состояние автоматически сохраняется в fsm_state.json)");
        Console.WriteLine("   /fsm resume — возобновление (загружает состояние из fsm_state.json)");
        Console.WriteLine("   /fsm reset — сбросить состояние и удалить fsm_state.json");
        Console.WriteLine("   /fsm save / load — сохранить / загрузить состояние");
        Console.WriteLine("   /fsm history — история переходов");
        Console.WriteLine("   /fsm dialog — история диалога");
        Console.WriteLine();
        Console.WriteLine("Примеры:");
        Console.WriteLine("   /fsm can Planning    — проверить, можно ли перейти к планированию");
        Console.WriteLine("   /fsm transition Planning — перейти к планированию (если разрешено)");
        Console.WriteLine("   /fsm allowed         — показать все допустимые переходы из текущего этапа");
        Console.WriteLine("   /fsm stages          — вывести полный список этапов");
        Console.WriteLine();
    }

    private static async Task ExecutePause(TaskStateMachine fsm)
    {
        var pauseFile = Path.Combine(AppContext.BaseDirectory, "fsm_state.json");
        var json = fsm.ToJson();
        File.WriteAllText(pauseFile, json, Encoding.UTF8);

        fsm.Pause();
        PrintYellow("⏸ Задача поставлена на паузу");
        Console.WriteLine($"   Состояние сохранено в: {pauseFile}");
        Console.WriteLine("   Для продолжения: /fsm resume");
        Console.WriteLine();
    }
}