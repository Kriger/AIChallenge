using AIChallenge.Core.Infrastructure;

namespace AIChallenge.Cli.Commands;

public sealed class AdaptiveCommand : CommandHandler
{
    public override string Name => "adaptive";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            PrintYellow("🔄 Адаптивное поведение:");
            Console.WriteLine(ctx.Agent.Adaptive.GetStatus());
            Console.WriteLine("   Команды: /adaptive on, /adaptive off, /adaptive reset, /adaptive threshold <0-1>");
            Console.WriteLine();
            return true;
        }

        var adaptiveCmd = parts[1].ToLowerInvariant();
        switch (adaptiveCmd)
        {
            case "on":
                ctx.Agent.Adaptive.Enabled = true;
                PrintGreen("✅ Адаптация включена");
                break;

            case "off":
                ctx.Agent.Adaptive.Enabled = false;
                PrintYellow("⚠️  Адаптация выключена");
                break;

            case "reset":
                ctx.Agent.Adaptive.Reset();
                PrintGreen("✅ Параметры сброшены к значениям по умолчанию");
                break;

            case "threshold":
                if (parts.Length < 3 || !double.TryParse(parts[2], out var threshold))
                {
                    PrintRed("❌ Формат: /adaptive threshold <0-1>. Пример: /adaptive threshold 0.2");
                    Console.WriteLine();
                    break;
                }
                if (threshold < 0 || threshold > 1)
                {
                    PrintRed("❌ Threshold должен быть в диапазоне 0–1");
                    Console.WriteLine();
                    break;
                }
                ctx.Agent.Adaptive.FailureThreshold = threshold;
                PrintGreen($"✅ Порог ошибок установлен: {threshold:P0}");
                break;

            default:
                PrintRed($"❌ Неизвестная команда адаптации: {adaptiveCmd}. Доступны: on, off, reset, threshold");
                break;
        }
        Console.WriteLine();
        return true;
    }
}

public sealed class PlannerCommand : CommandHandler
{
    public override string Name => "planner";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        PrintYellow("📋 Планировщик:");
        Console.WriteLine($"   Включён: {(ctx.Agent.Planner != null ? "да" : "нет")}");
        Console.WriteLine($"   MinComplexityLength: {ctx.Agent.Planner?.MinComplexityLength}");
        Console.WriteLine($"   MaxTasks: {ctx.Agent.Planner?.MaxTasks}");
        Console.WriteLine();
        Console.WriteLine("   Запросы длиннее MinComplexityLength и содержащие ключевые слова");
        Console.WriteLine("   автоматически разбиваются на подзадачи.");
        Console.WriteLine("   Ключевые слова: анализируй, сравни, перечисли, составь, создай,");
        Console.WriteLine("   разработай, изучи, каждый, все, какие, почему, как, объясни,");
        Console.WriteLine("   разбей, декомпозируй, пошагово, по шагам, последовательно");
        Console.WriteLine();
        return true;
    }
}

public sealed class PlanCommand : CommandHandler
{
    public override string Name => "plan";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (ctx.Agent.History.Count < 1)
        {
            PrintRed("❌ Нет сообщений в истории для планирования");
            Console.WriteLine();
            return true;
        }

        var lastUserMsg = ctx.Agent.History
            .Where(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();

        if (lastUserMsg is null)
        {
            PrintRed("❌ Нет сообщений пользователя в истории");
            Console.WriteLine();
            return true;
        }

        PrintYellow($"📋 Ручной запуск планировщика для: \"{lastUserMsg.Content[..Math.Min(60, lastUserMsg.Content.Length)]}\"");

        Console.WriteLine($"   До: рабочая память содержит {ctx.Agent.MemoryManager.Working.Count} записей");

        var facts = ctx.Agent.MemoryManager.LongTerm.FindRelevant(lastUserMsg.Content);
        Console.WriteLine($"   Найдено {facts.Count} фактов для контекста");

        var plan = await ctx.Agent.Planner!.CreatePlanAsync(lastUserMsg!.Content, facts);
        Console.WriteLine($"   CreatePlanAsync вернул: {(plan == null ? "null" : $"{plan.Tasks.Count} задач")}");

        if (plan is null || plan.Tasks.Count == 0)
        {
            PrintYellow("   LLM не создал план (запрос слишком простой)");
            ctx.Agent.MemoryManager.Working.Save("request", lastUserMsg.Content, "request");
            Console.WriteLine($"   Запрос сохранён в рабочую память. Всего записей: {ctx.Agent.MemoryManager.Working.Count}");
        }
        else
        {
            PrintGreen($"   ✅ План создан: {plan.Tasks.Count} подзадач");
            foreach (var task in plan.Tasks)
            {
                Console.WriteLine($"     {task.Id}. {task.Description}");
            }
            ctx.Agent.MemoryManager.Working.SavePlan(plan);
            ctx.Agent.MemoryManager.Working.Save("request", lastUserMsg.Content, "request");
            Console.WriteLine();
            Console.WriteLine($"   План сохранён в рабочую память. Всего записей: {ctx.Agent.MemoryManager.Working.Count}");
            Console.WriteLine($"   Текущая задача: {ctx.Agent.MemoryManager.Working.CurrentTaskId}");
        }
        Console.WriteLine();
        return true;
    }
}
