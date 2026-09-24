using AIChallenge.Models;
using AIChallenge.Core;
namespace AIChallenge.Cli.Commands;

using AIChallenge.Core.Infrastructure;

public sealed class SaveCommand : CommandHandler
{
    public override string Name => "save";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        PrintYellow("💾 Сохранение контекста...");

        var files = new List<string>();
        ContextPersistence.SaveContext(ctx.Agent);

        var memoryDir = Path.Combine(AppContext.BaseDirectory, "memory");
        if (Directory.Exists(memoryDir))
        {
            foreach (var filePath in Directory.GetFiles(memoryDir, "*.json", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(memoryDir, filePath);
                var info = new FileInfo(filePath);
                files.Add($"   memory/{relPath,-28} {info.Length,8} байт");
            }
        }

        PrintGreen("✅ Контекст сохранён в папку memory/:");
        foreach (var f in files)
            Console.WriteLine(f);
        Console.WriteLine();
        return true;
    }
}

public sealed class FactsCommand : CommandHandler
{
    public override string Name => "facts";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            PrintRed("❌ Команды фактов: /facts list, /facts save <ключ> <значение>, /facts delete <ключ>");
            Console.WriteLine("   Работают для StickyFacts и Branching стратегий.");
            Console.WriteLine();
            return true;
        }

        var factsCommand = parts[1].ToLowerInvariant();
        switch (factsCommand)
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

            default:
                PrintRed($"❌ Неизвестная команда фактов: {factsCommand}. Доступны: list, save, delete");
                Console.WriteLine();
                break;
        }
        return true;
    }

    private async Task ExecuteListAsync(CommandContext ctx)
    {
        PrintYellow("🧠 Факты:");

        var storage = ctx.Agent.ContextManager.ActiveFactStorage;
        if (storage == null)
        {
            PrintYellow("   Ни одна стратегия с фактами не активна.");
            Console.WriteLine("   Переключитесь: /context strategy sticky или /context strategy branching");
        }
        else if (storage.Facts.Count == 0)
        {
            Console.WriteLine("   (пусто)");
        }
        else
        {
            foreach (var kvp in storage.Facts)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"   → {kvp.Key}: ");
                Console.ResetColor();
                Console.WriteLine(kvp.Value);
            }
            Console.WriteLine($"   Всего фактов: {storage.Facts.Count}");
        }
        Console.WriteLine();
    }

    private async Task ExecuteSaveAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 4)
        {
            PrintRed("❌ Формат: /facts save <ключ> <значение>");
            Console.WriteLine();
            return;
        }

        var storage = ctx.Agent.ContextManager.ActiveFactStorage;
        if (storage == null)
        {
            PrintYellow("⚠️  Ни одна стратегия с фактами не активна.");
            Console.WriteLine("   Переключитесь: /context strategy sticky или /context strategy branching");
        }
        else
        {
            storage.SaveFact(parts[2], parts[3]);
            PrintGreen($"✅ Факт сохранён: {parts[2]} = \"{parts[3]}\"");
        }
        Console.WriteLine();
    }

    private async Task ExecuteDeleteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed("❌ Формат: /facts delete <ключ>");
            Console.WriteLine();
            return;
        }

        var storage = ctx.Agent.ContextManager.ActiveFactStorage;
        if (storage == null)
        {
            PrintYellow("⚠️  Ни одна стратегия с фактами не активна.");
            Console.WriteLine("   Переключитесь: /context strategy sticky или /context strategy branching");
        }
        else if (storage.DeleteFact(parts[2]))
        {
            PrintGreen($"✅ Факт удалён: {parts[2]}");
        }
        else
        {
            PrintRed($"❌ Факт не найден: {parts[2]}");
        }
        Console.WriteLine();
    }
}

public sealed class BranchCommand : CommandHandler
{
    public override string Name => "branch";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            PrintRed("❌ Команды веток: /branch list, /branch create <имя>, /branch switch <id>, /branch checkpoint <имя>, /branch create-from <cp-id> <имя>, /branch delete <id>");
            Console.WriteLine();
            return true;
        }

        var branchCommand = parts[1].ToLowerInvariant();
        switch (branchCommand)
        {
            case "list":
                await ExecuteListAsync(ctx);
                break;

            case "create":
                await ExecuteCreateAsync(parts, ctx);
                break;

            case "switch":
                await ExecuteSwitchAsync(parts, ctx);
                break;

            case "checkpoint":
                await ExecuteCheckpointAsync(parts, ctx);
                break;

            case "create-from":
                await ExecuteCreateFromAsync(parts, ctx);
                break;

            case "delete":
                await ExecuteDeleteAsync(parts, ctx);
                break;

            default:
                PrintRed($"❌ Неизвестная команда веток: {branchCommand}. Доступны: list, create, create-from, switch, checkpoint, delete");
                Console.WriteLine();
                break;
        }
        return true;
    }

    private async Task ExecuteListAsync(CommandContext ctx)
    {
        PrintYellow("🌿 Ветки диалога:");
        if (ctx.Agent.ContextManager.Branching is { } br)
            Console.WriteLine(br.GetStatus());
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("   Branching не активна. Переключитесь: /context strategy branching");
            Console.ResetColor();
        }
        Console.WriteLine();
    }

    private async Task ExecuteCreateAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed("❌ Формат: /branch create <имя>");
            Console.WriteLine();
            return;
        }
        if (ctx.Agent.ContextManager.Branching is { } branching2)
        {
            try
            {
                var branchId = branching2.CreateBranch(parts[2]);
                branching2.SwitchBranch(branchId);
                ctx.Agent.ContextManager.SetStrategy(ContextStrategy.Branching);
                ctx.Agent.ContextManager.Config.Strategy = ContextStrategy.Branching;
                PrintGreen($"✅ Ветка создана и активирована: \"{parts[2]}\" (id: {branchId})");
            }
            catch (Exception ex)
            {
                PrintRed($"❌ Ошибка: {ex.Message}");
            }
        }
        else
        {
            PrintYellow("⚠️  Branching не активна. Переключитесь: /context strategy branching");
        }
        Console.WriteLine();
    }

    private async Task ExecuteSwitchAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed("❌ Формат: /branch switch <id>");
            Console.WriteLine();
            return;
        }
        if (ctx.Agent.ContextManager.Branching is { } branching3)
        {
            try
            {
                branching3.SwitchBranch(parts[2]);
                PrintGreen($"✅ Переключено на ветку: {branching3.ActiveBranchName} ({parts[2]})");
            }
            catch (Exception ex)
            {
                PrintRed($"❌ Ошибка: {ex.Message}");
            }
        }
        else
        {
            PrintYellow("⚠️  Branching не активна. Переключитесь: /context strategy branching");
        }
        Console.WriteLine();
    }

    private async Task ExecuteCheckpointAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed("❌ Формат: /branch checkpoint <имя>");
            Console.WriteLine();
            return;
        }
        if (ctx.Agent.ContextManager.Branching is { } branching4)
        {
            try
            {
                var cpName = parts[2].Trim('\"', '\'');
                var cp = branching4.CreateCheckpoint(cpName);
                PrintGreen($"✅ Checkpoint создан: \"{cp.Name}\" (id: {cp.Id}, сообщений: {cp.MessageCount})");
            }
            catch (Exception ex)
            {
                PrintRed($"❌ Ошибка: {ex.Message}");
            }
        }
        else
        {
            PrintYellow("⚠️  Branching не активна. Переключитесь: /context strategy branching");
        }
        Console.WriteLine();
    }

    private async Task ExecuteCreateFromAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 4)
        {
            PrintRed("❌ Формат: /branch create-from <cp-id> <имя-ветки>");
            Console.WriteLine("   Пример: /branch create-from cp-1 \"вариант с Redis\"");
            Console.WriteLine();
            return;
        }
        if (ctx.Agent.ContextManager.Branching is { } branching6)
        {
            try
            {
                var branchId = branching6.CreateBranchFromCheckpoint(parts[2], parts[3].Trim('\"', '\''));
                branching6.SwitchBranch(branchId);
                ctx.Agent.ContextManager.SetStrategy(ContextStrategy.Branching);
                ctx.Agent.ContextManager.Config.Strategy = ContextStrategy.Branching;
                PrintGreen($"✅ Ветка создана от checkpoint {parts[2]}: \"{parts[3]}\" (id: {branchId})");
            }
            catch (Exception ex)
            {
                PrintRed($"❌ Ошибка: {ex.Message}");
            }
        }
        else
        {
            PrintYellow("⚠️  Branching не активна. Переключитесь: /context strategy branching");
        }
        Console.WriteLine();
    }

    private async Task ExecuteDeleteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3)
        {
            PrintRed("❌ Формат: /branch delete <id>");
            Console.WriteLine();
            return;
        }
        if (ctx.Agent.ContextManager.Branching is { } branching5)
        {
            try
            {
                branching5.DeleteBranch(parts[2]);
                PrintGreen($"✅ Ветка удалена: {parts[2]}. Активна: {branching5.ActiveBranchName}");
            }
            catch (Exception ex)
            {
                PrintRed($"❌ Ошибка: {ex.Message}");
            }
        }
        else
        {
            PrintYellow("⚠️  Branching не активна. Переключитесь: /context strategy branching");
        }
        Console.WriteLine();
    }
}
