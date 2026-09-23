using AIChallenge.Core.Infrastructure;

namespace AIChallenge.Cli.Commands;

public sealed class MemoryCommand : CommandHandler
{
    public override string Name => "memory";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            PrintRed("❌ Команды памяти: /memory list, /memory save <ключ> <значение>, /memory delete <ключ>, /memory search <запрос>, /memory status, /memory extract");
            Console.WriteLine();
            return true;
        }

        var memoryCommand = parts[1].ToLowerInvariant();
        switch (memoryCommand)
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

            case "search":
                await ExecuteSearchAsync(parts, ctx);
                break;

            case "status":
                await ExecuteStatusAsync(ctx);
                break;

            case "extract":
                await ExecuteExtractAsync(ctx);
                break;

            default:
                PrintRed($"❌ Неизвестная команда памяти: {memoryCommand}. Доступны: list, save, delete, search, status, extract");
                Console.WriteLine();
                break;
        }
        return true;
    }

    private async Task ExecuteListAsync(CommandContext ctx)
    {
        PrintYellow("🧠 Память агента:");
        if (ctx.Agent.Memory.Count == 0)
        {
            Console.WriteLine("   (пусто)");
        }
        else
        {
            foreach (var kvp in ctx.Agent.Memory.All)
            {
                var fact = kvp.Value;
                var color = fact.Source switch
                {
                    "user" => ConsoleColor.Green,
                    "extracted" => ConsoleColor.Cyan,
                    "explicit" => ConsoleColor.Magenta,
                    _ => ConsoleColor.White,
                };
                Console.ForegroundColor = color;
                Console.Write($"   [{fact.Source,-9}] ");
                Console.ResetColor();
                Console.Write($"{fact.Key}: ");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(fact.Value);
                Console.ResetColor();
            }
        }
        Console.WriteLine();
        Console.WriteLine($"   Всего фактов: {ctx.Agent.Memory.Count}");
        Console.WriteLine();
    }

    private async Task ExecuteSaveAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 4)
        {
            PrintRed("❌ Формат: /memory save <ключ> <значение>");
            Console.WriteLine();
            return;
        }
        ctx.Agent.Memory.Save(parts[2], parts[3], "explicit");
        PrintGreen($"✅ Факт сохранён: {parts[2]} = \"{parts[3]}\"");
        Console.WriteLine();
    }

    private async Task ExecuteDeleteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed("❌ Формат: /memory delete <ключ>");
            Console.WriteLine();
            return;
        }
        if (ctx.Agent.Memory.Delete(parts[2]))
        {
            PrintGreen($"✅ Факт удалён: {parts[2]}");
        }
        else
        {
            PrintRed($"❌ Факт не найден: {parts[2]}");
        }
        Console.WriteLine();
    }

    private async Task ExecuteSearchAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed("❌ Формат: /memory search <запрос>");
            Console.WriteLine();
            return;
        }
        var searchQuery = string.Join(" ", parts.Skip(2));
        var searchResults = ctx.Agent.MemoryManager.LongTerm.FindRelevant(searchQuery);
        PrintYellow($"🔍 Поиск по долгосрочной памяти: \"{searchQuery}\"");
        if (searchResults.Count == 0)
        {
            Console.WriteLine("   Ничего не найдено");
        }
        else
        {
            foreach (var fact in searchResults)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"   → {fact.Key}: ");
                Console.ResetColor();
                Console.WriteLine(fact.Value);
            }
        }
        Console.WriteLine();
    }

    private async Task ExecuteStatusAsync(CommandContext ctx)
    {
        PrintMagenta("🧠 Статус всех слоёв памяти:");
        Console.WriteLine();

        // Краткосрочная
        PrintGreen("── Краткосрочная память (диалог) ──");
        Console.WriteLine($"   Записей: {ctx.Agent.MemoryManager.ShortTerm.Count}");
        Console.WriteLine($"   Лимит: {ctx.Agent.MemoryManager.ShortTerm.MaxSize}");
        if (ctx.Agent.MemoryManager.ShortTerm.Count > 0)
        {
            var last = ctx.Agent.MemoryManager.ShortTerm.Last;
            if (last is not null)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"   Последняя: [{last.Role}] {last.Content[..Math.Min(80, last.Content.Length)]}");
                Console.ResetColor();
            }
        }
        Console.WriteLine();

        // Рабочая
        PrintCyan("── Рабочая память (текущая задача) ──");
        Console.WriteLine($"   Записей: {ctx.Agent.MemoryManager.Working.Count}");
        Console.WriteLine($"   Задача: {ctx.Agent.MemoryManager.Working.CurrentTaskId ?? "нет"}");
        Console.WriteLine($"   Статус: {ctx.Agent.MemoryManager.Working.CurrentTaskStatus ?? "нет"}");
        if (ctx.Agent.MemoryManager.Working.Count > 0)
        {
            var entries = ctx.Agent.MemoryManager.Working.LoadCurrentTaskData();
            foreach (var entry in entries.Take(5))
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write($"   [{entry.Type,-8}] {entry.Key}: ");
                Console.ResetColor();
                Console.WriteLine(entry.Value[..Math.Min(60, entry.Value.Length)]);
            }
            if (entries.Count > 5)
                Console.WriteLine($"   ... и ещё {entries.Count - 5} записей");
        }
        Console.WriteLine();

        // Долгосрочная
        PrintYellow("── Долгосрочная память (знания) ──");
        Console.WriteLine($"   Фактов: {ctx.Agent.MemoryManager.LongTerm.Count}");
        if (ctx.Agent.MemoryManager.LongTerm.Count > 0)
        {
            foreach (var kvp in ctx.Agent.MemoryManager.LongTerm.All.Take(5))
            {
                var fact = kvp.Value;
                var color = fact.Source switch
                {
                    "user" => ConsoleColor.Green,
                    "extracted" => ConsoleColor.Cyan,
                    "explicit" => ConsoleColor.Magenta,
                    _ => ConsoleColor.White,
                };
                Console.ForegroundColor = color;
                Console.Write($"   [{fact.Source,-9}] ");
                Console.ResetColor();
                Console.Write($"{fact.Key}: ");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(fact.Value[..Math.Min(60, fact.Value.Length)]);
                Console.ResetColor();
            }
            if (ctx.Agent.MemoryManager.LongTerm.Count > 5)
                Console.WriteLine($"   ... и ещё {ctx.Agent.MemoryManager.LongTerm.Count - 5} фактов");
        }
        Console.WriteLine();
    }

    private async Task ExecuteExtractAsync(CommandContext ctx)
    {
        PrintYellow("🧠 Извлечение фактов из полного диалога...");

        var dialogue = ctx.Agent.MemoryManager.ShortTerm.GetAll();
        if (dialogue.Count == 0)
        {
            Console.WriteLine("   Диалог пуст");
            Console.WriteLine();
            return;
        }

        var count = await ctx.Agent.ExtractFactsExplicitAsync(dialogue);
        Console.ForegroundColor = count > 0 ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine(count > 0
            ? $"✅ Извлечено {count} факт(ов) в долгосрочную память"
            : "ℹ️ Факты не найдены (LLM не определил значимых фактов)");
        Console.ResetColor();
        Console.WriteLine();
    }
}
