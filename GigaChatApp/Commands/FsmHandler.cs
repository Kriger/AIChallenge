using GigaChatApp.Models;
using GigaChatApp.Infrastructure;
using GigaChatApp.Services;

namespace GigaChatApp.Commands;

/// <summary>
/// Static helper methods for FSM processing (moved from Program.cs).
/// </summary>
public static class FsmHandler
{
    /// <summary>
    /// Обработка ввода через FSM (сбор требований → планирование → выполнение).
    /// </summary>
    public static async Task HandleFsmInputAsync(string input, TaskStateMachine fsm, GigaChatConfig config)
    {
        var reqStatus = fsm.GetRequirementsStatus();

        if (reqStatus is not null && !reqStatus.IsComplete)
        {
            var answeredCount = reqStatus.AnsweredQuestions;
            var totalQuestions = reqStatus.TotalQuestions;

            // Если вопросы ещё не заданы — инициализируем
            if (answeredCount == 0 && fsm.RequirementsContext is null)
            {
                // Проверяем инварианты ДО генерации вопросов
                var profile = fsm.AgentProfile;
                if (profile?.Invariants.Count > 0)
                {
                    var violation = Planner.CheckInvariantViolation(input, profile.Invariants);
                    if (violation is not null)
                    {
                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"⛔ Я не могу выполнить этот запрос, потому что он нарушает инвариант:");
                        Console.WriteLine();
                        Console.WriteLine($"  ⛔ {violation}");
                        Console.WriteLine();
                        Console.WriteLine("Инварианты — непреложные правила проекта. Давай найдём альтернативу,");
                        Console.WriteLine("которая удовлетворяет твою потребность, но остаётся в рамках принятых решений.");
                        Console.ResetColor();
                        Console.WriteLine();
                        return;
                    }
                }

                Console.WriteLine("📝 Начинаю сбор требований...");
                Console.WriteLine();

                var nextQuestion = await fsm.InitializeRequirementsAsync(input);

                // Пересчитываем статус после инициализации
                var initStatus = fsm.GetRequirementsStatus();
                totalQuestions = initStatus?.TotalQuestions ?? 0;

                if (nextQuestion is null)
                {
                    PrintGreen("✅ Все вопросы заданы. Перехожу к планированию...");
                    Console.WriteLine();
                    var fsmResult = await fsm.RunAutoAsync(input);
                    PrintFsmResult(fsmResult.IsSuccess, fsmResult.Message);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"   Вопрос 1/{totalQuestions}:");
                    Console.ResetColor();
                    Console.WriteLine($"   {nextQuestion}");
                    Console.WriteLine();
                    Console.WriteLine("   Введите ответ (или /skip чтобы пропустить вопросы):");
                }
            }
            else
            {
                // Пользователь отвечает на вопрос
                Console.WriteLine($"💬 Ваш ответ: \"{input}\"");
                Console.WriteLine();

                fsm.ReceiveAnswer(input);

                var nextQuestion = fsm.AskNextQuestion();
                var newStatus = fsm.GetRequirementsStatus();

                if (nextQuestion is null)
                {
                    PrintGreen($"✅ Все {totalQuestions} вопросов задано. Перехожу к планированию...");
                    Console.WriteLine();
                    var fsmResult = await fsm.RunAutoAsync(input);
                    PrintFsmResult(fsmResult.IsSuccess, fsmResult.Message);
                }
                else
                {
                    if (newStatus is not null)
                    {
                        PrintGreen($"✅ Ответ принят. ({newStatus.AnsweredQuestions}/{newStatus.TotalQuestions})");
                        Console.WriteLine();

                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"   Вопрос {newStatus.AnsweredQuestions + 1}/{newStatus.TotalQuestions}:");
                        Console.ResetColor();
                        Console.WriteLine($"   {nextQuestion}");
                        Console.WriteLine();
                        Console.WriteLine("   Введите ответ (или /skip чтобы пропустить вопросы):");
                    }
                    else
                    {
                        PrintGreen($"✅ Ответ принят.");
                        Console.WriteLine();
                        Console.WriteLine($"   {nextQuestion}");
                        Console.WriteLine();
                        Console.WriteLine("   Введите ответ (или /skip чтобы пропустить вопросы):");
                    }
                }
            }
        }
        else
        {
            var fsmResult = await fsm.RunAutoAsync(input);
            PrintFsmResult(fsmResult.IsSuccess, fsmResult.Message);
        }
    }

    /// <summary>
    /// Обработка обычного запроса (без команд и FSM).
    /// </summary>
    public static async Task HandleNormalInputAsync(string input, ChatAgent agent)
    {
        PrintGray("⏳ Думает...");
        var result = await agent.ProcessRequestAsync(input);

        var logs = agent.Logger.FlushBuffer();
        AgentLogger.PrintBufferedMessages(logs);

        if (!result.IsSuccess)
        {
            Console.WriteLine();
            PrintRed($"❌ Ошибка: {result.Error}");
            Console.WriteLine();
            return;
        }

        var duration = result.Duration.TotalSeconds < 1
            ? $"{result.Duration.TotalMilliseconds:F0} мс"
            : $"{result.Duration.TotalSeconds:F1} с";

        Console.WriteLine();
        PrintGreen("🤖 GigaChat:");
        Console.WriteLine($"   {result.Answer}");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"   ⏱ {duration}");
        if (result.Source == Source.Cache)
        {
            Console.Write("  |  [из кэша]  |  Токенов не потреблено");
        }
        else if (result.Usage is not null)
        {
            var u = result.Usage;
            Console.Write($"  |  📊 Токены: {u.PromptTokens} в → {u.CompletionTokens} out → {u.TotalTokens} всего");
        }
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine();
    }

    /// <summary>
    /// Вывод результата FSM-операции.
    /// </summary>
    public static void PrintFsmResult(bool isSuccess, string message)
    {
        Console.WriteLine();
        Console.ForegroundColor = isSuccess ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine($"✅ {message}");
        Console.ResetColor();
        Console.WriteLine();
    }
}
