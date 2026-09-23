using AIChallenge.Core.Infrastructure;

namespace AIChallenge.Cli.Commands;

public sealed class ShortTermCommand : CommandHandler
{
    public override string Name => "st";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            PrintYellow("🗨 Краткосрочная память (текущий диалог):");
            Console.WriteLine($"   Записей: {ctx.Agent.MemoryManager.ShortTerm.Count}");
            Console.WriteLine($"   Максимум: {ctx.Agent.MemoryManager.ShortTerm.MaxSize}");
            Console.WriteLine("   Команды: /st list, /st recent <N>, /st search <запрос>, /st clear");
            Console.WriteLine();
            return true;
        }

        var stCmd = parts[1].ToLowerInvariant();
        switch (stCmd)
        {
            case "list":
                await ExecuteListAsync(ctx);
                break;

            case "recent":
                await ExecuteRecentAsync(parts, ctx);
                break;

            case "search":
                await ExecuteSearchAsync(parts, ctx);
                break;

            case "clear":
                ctx.Agent.MemoryManager.ClearShortTerm();
                PrintGreen("✅ Краткосрочная память очищена");
                Console.WriteLine();
                break;

            default:
                PrintRed($"❌ Неизвестная команда: {stCmd}. Доступны: list, recent, search, clear");
                Console.WriteLine();
                break;
        }
        return true;
    }

    private async Task ExecuteListAsync(CommandContext ctx)
    {
        PrintYellow("🗨 Краткосрочная память:");
        var entries = ctx.Agent.MemoryManager.ShortTerm.GetAll();
        if (entries.Count == 0)
        {
            Console.WriteLine("   (пусто)");
        }
        else
        {
            foreach (var entry in entries)
            {
                var color = entry.Role switch
                {
                    "user" => ConsoleColor.Green,
                    "assistant" => ConsoleColor.Cyan,
                    "system" => ConsoleColor.Gray,
                    _ => ConsoleColor.White,
                };
                Console.ForegroundColor = color;
                Console.Write($"   [{entry.Role,-10}] ");
                Console.ResetColor();
                Console.WriteLine(entry.Content);
            }
        }
        Console.WriteLine();
    }

    private async Task ExecuteRecentAsync(string[] parts, CommandContext ctx)
    {
        int count = Math.Min(20, ctx.Agent.MemoryManager.ShortTerm.Count);
        if (parts.Length >= 3 && int.TryParse(parts[2], out var requested))
        {
            count = Math.Max(1, Math.Min(requested, ctx.Agent.MemoryManager.ShortTerm.Count));
        }
        PrintYellow($"🗨 Последние {count} записей:");
        var recent = ctx.Agent.MemoryManager.ShortTerm.GetRecent(count);
        foreach (var entry in recent)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"   [{entry.Role,-10}] ");
            Console.ResetColor();
            Console.WriteLine(entry.Content);
        }
        Console.WriteLine();
    }

    private async Task ExecuteSearchAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed("❌ Формат: /st search <запрос>");
            Console.WriteLine();
            return;
        }
        var query = string.Join(" ", parts.Skip(2));
        var results = ctx.Agent.MemoryManager.ShortTerm.Search(query);
        PrintYellow($"🔍 Поиск в краткосрочной памяти: \"{query}\"");
        if (results.Count == 0)
        {
            Console.WriteLine("   Ничего не найдено");
        }
        else
        {
            foreach (var entry in results)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"   → [{entry.Role,-10}] ");
                Console.ResetColor();
                Console.WriteLine(entry.Content);
            }
        }
        Console.WriteLine();
    }
}

public sealed class WorkingCommand : CommandHandler
{
    public override string Name => "wt";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            PrintYellow("⚙ Рабочая память (текущая задача):");
            Console.WriteLine($"   Записей: {ctx.Agent.MemoryManager.Working.Count}");
            Console.WriteLine($"   Задача: {ctx.Agent.MemoryManager.Working.CurrentTaskId ?? "нет"} [{ctx.Agent.MemoryManager.Working.CurrentTaskStatus ?? "нет"}]");
            Console.WriteLine("   Команды: /wt list, /wt save <ключ> <значение>, /wt delete <ключ>, /wt task <id>");
            Console.WriteLine();
            return true;
        }

        var wtCmd = parts[1].ToLowerInvariant();
        switch (wtCmd)
        {
            case "list":
                await ExecuteListAsync(ctx);
                break;

            case "save":
                await ExecuteSaveAsync(parts, ctx);
                break;

            case "delete":
                await ExecuteDeleteAsync(parts, ctx);
                break;

            case "task":
                await ExecuteTaskAsync(parts, ctx);
                break;

            case "clear":
                ctx.Agent.MemoryManager.ClearWorking();
                PrintGreen("✅ Рабочая память очищена");
                Console.WriteLine();
                break;

            default:
                PrintRed($"❌ Неизвестная команда: {wtCmd}. Доступны: list, save, delete, task, clear");
                Console.WriteLine();
                break;
        }
        return true;
    }

    private async Task ExecuteListAsync(CommandContext ctx)
    {
        PrintYellow("⚙ Рабочая память:");
        var entries = ctx.Agent.MemoryManager.Working.LoadCurrentTaskData();
        if (entries.Count == 0)
        {
            Console.WriteLine("   (пусто)");
        }
        else
        {
            foreach (var entry in entries)
            {
                var color = entry.Type switch
                {
                    "plan" => ConsoleColor.Magenta,
                    "task" => ConsoleColor.Cyan,
                    "result" => ConsoleColor.Green,
                    "fact" => ConsoleColor.Yellow,
                    _ => ConsoleColor.White,
                };
                Console.ForegroundColor = color;
                Console.Write($"   [{entry.Type,-8}] ");
                Console.ResetColor();
                Console.Write($"{entry.Key}: ");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(entry.Value);
                Console.ResetColor();
            }
        }
        Console.WriteLine();
    }

    private async Task ExecuteSaveAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 4)
        {
            PrintRed("❌ Формат: /wt save <ключ> <значение>");
            Console.WriteLine();
            return;
        }
        ctx.Agent.MemoryManager.Save(MemoryType.Working, parts[2], parts[3], "user");
        PrintGreen($"✅ Сохранено в рабочую память: {parts[2]} = \"{parts[3]}\"");
        Console.WriteLine();
    }

    private async Task ExecuteDeleteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed("❌ Формат: /wt delete <ключ>");
            Console.WriteLine();
            return;
        }
        if (ctx.Agent.MemoryManager.Working.Delete(parts[2]))
        {
            PrintGreen($"✅ Удалено из рабочей памяти: {parts[2]}");
        }
        else
        {
            PrintRed($"❌ Не найдено: {parts[2]}");
        }
        Console.WriteLine();
    }

    private async Task ExecuteTaskAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed("❌ Формат: /wt task <start|complete|fail> [id]");
            Console.WriteLine();
            return;
        }

        var taskAction = parts[2];
        switch (taskAction)
        {
            case "start":
                var taskId = parts.Length >= 4 ? parts[3] : Guid.NewGuid().ToString("N")[..8];
                ctx.Agent.MemoryManager.StartTask(taskId);
                PrintGreen($"✅ Начата задача: {taskId}");
                break;

            case "complete":
                var completeResult = parts.Length >= 4 ? string.Join(" ", parts.Skip(3)) : null;
                ctx.Agent.MemoryManager.CompleteTask(completeResult);
                PrintGreen("✅ Текущая задача завершена");
                break;

            case "fail":
                var reason = parts.Length >= 4 ? string.Join(" ", parts.Skip(3)) : "не указано";
                ctx.Agent.MemoryManager.FailTask(reason);
                PrintRed($"❌ Текущая задача провалена: {reason}");
                break;

            default:
                PrintRed($"❌ Неизвестное действие: {taskAction}. Доступны: start, complete, fail");
                break;
        }
        Console.WriteLine();
    }
}
