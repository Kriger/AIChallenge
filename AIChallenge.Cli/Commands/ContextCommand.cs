using AIChallenge.Models;
using AIChallenge.Core.Infrastructure;

namespace AIChallenge.Cli.Commands;

public sealed class ContextCommand : CommandHandler
{
    public override string Name => "context";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 2)
        {
            await ExecuteStatusAsync(ctx);
            return true;
        }

        var contextCommand = parts[1].ToLowerInvariant();
        switch (contextCommand)
        {
            case "strategy":
                await ExecuteStrategyAsync(parts, ctx);
                break;

            case "on":
                ctx.Agent.ContextManager.Config.Enabled = true;
                ctx.Agent.Metrics.ContextCompressionEnabled = true;
                PrintGreen("✅ Управление контекстом включено (сжатие с summary)");
                Console.WriteLine();
                break;

            case "off":
                ctx.Agent.ContextManager.Config.Enabled = false;
                ctx.Agent.Metrics.ContextCompressionEnabled = false;
                PrintYellow("⚠️  Управление контекстом выключено (полная история)");
                Console.WriteLine();
                break;

            case "reset":
                ResetContext(ctx);
                PrintGreen("✅ Параметры контекста сброшены, история очищена");
                Console.WriteLine();
                break;

            case "recent":
                await ExecuteRecentAsync(parts, ctx);
                break;

            case "interval":
                await ExecuteIntervalAsync(parts, ctx);
                break;

            case "report":
                await ExecuteReportAsync(ctx);
                break;

            default:
                PrintRed($"❌ Неизвестная команда контекста: {contextCommand}. Доступны: strategy, on, off, reset, recent, interval, report");
                Console.WriteLine();
                break;
        }
        return true;
    }

    private async Task ExecuteStatusAsync(CommandContext ctx)
    {
        PrintYellow("📦 Управление контекстом:");
        var cm = ctx.Agent.ContextManager;
        var cfg = cm.Config;
        Console.WriteLine($"   Включено: {(cfg.Enabled ? "да" : "нет")}");
        Console.WriteLine($"   Активная стратегия: {cfg.Strategy}");
        Console.WriteLine();

        PrintCyan("   Настройки стратегий:");
        Console.WriteLine($"   SlidingWindow:     window={ctx.Config.Context.SlidingWindow.WindowSize}");
        Console.WriteLine($"   StickyFacts:       window={ctx.Config.Context.StickyFacts.WindowSize}, maxFacts={ctx.Config.Context.StickyFacts.MaxFacts}");
        Console.WriteLine($"   Branching:         maxBranches={ctx.Config.Context.Branching.MaxBranches}, maxCheckpoints={ctx.Config.Context.Branching.MaxCheckpoints}");
        Console.WriteLine($"   Summary (legacy):  interval={ctx.Config.Context.Summary.Interval}, maxSummaries={ctx.Config.Context.Summary.MaxSummaries}");

        Console.WriteLine();
        PrintCyan("   Статус активной стратегии:");
        Console.WriteLine(cm.GetStrategyStatus());

        if (cfg.Strategy == ContextStrategy.SlidingWindow || cfg.Strategy == ContextStrategy.Branching)
        {
            Console.WriteLine($"   Summary блоков (legacy): {cm.SummaryCount}");
        }

        if (ctx.Agent.Metrics.ContextComparison.ComparisonCount > 0)
        {
            PrintGreen("📊 Метрики сжатия:");
            var comp = ctx.Agent.Metrics.ContextComparison;
            Console.WriteLine($"   Сравнений: {comp.ComparisonCount}");
            Console.WriteLine($"   Токены до:     {comp.TotalOriginalTokens,10:N0}");
            Console.WriteLine($"   Токены после:  {comp.TotalCompressedTokens,10:N0}");
            Console.WriteLine($"   Экономия:      {comp.TotalTokenSavings,10:N0} ({comp.TokenSavingsPercent:F1}%)");
            Console.WriteLine();
        }

        Console.WriteLine("   Команды:");
        Console.WriteLine("   /context strategy — список стратегий");
        Console.WriteLine("   /context strategy <sliding|sticky|branching> — переключить");
        Console.WriteLine("   /context on/off (legacy summary)");
        Console.WriteLine("   /context recent <N>, /context interval <N>, /context report");
        Console.WriteLine();
    }

    private async Task ExecuteStrategyAsync(string[] parts, CommandContext ctx)
    {
        var strategyArg = parts.Length >= 3 ? parts[2] : "";

        if (strategyArg == "")
        {
            PrintYellow("📦 Стратегии управления контекстом:");
            Console.WriteLine();
            Console.WriteLine("   1. sliding — Sliding Window");
            Console.WriteLine("      Хранит только последние N сообщений, остальное отбрасывается.");
            Console.WriteLine("      Быстрая, без LLM-вызовов.");
            Console.WriteLine();
            Console.WriteLine("   2. sticky — Sticky Facts");
            Console.WriteLine("      Отдельный блок фактов (ключ-значение) + последние N сообщений.");
            Console.WriteLine("      Факты обновляются LLM после каждого сообщения пользователя.");
            Console.WriteLine();
            Console.WriteLine("   3. branching — Branching");
            Console.WriteLine("      Ветвление диалога: checkpoints, создание веток, переключение.");
            Console.WriteLine();
            Console.WriteLine($"   Текущая стратегия: {ctx.Agent.ContextManager.Config.Strategy}");
            Console.WriteLine();
        }
        else
        {
            var strategyName = strategyArg.ToLowerInvariant();
            ContextStrategy newStrategy;
            switch (strategyName)
            {
                case "sliding":
                case "slidingwindow":
                    newStrategy = ContextStrategy.SlidingWindow;
                    break;
                case "sticky":
                case "stickyfacts":
                    newStrategy = ContextStrategy.StickyFacts;
                    break;
                case "branching":
                    newStrategy = ContextStrategy.Branching;
                    break;
                default:
                    PrintRed($"❌ Неизвестная стратегия: '{strategyArg}'. Доступны: sliding, sticky, branching");
                    Console.WriteLine();
                    return;
            }

            ctx.Agent.ContextManager.SetStrategy(newStrategy);
            ctx.Agent.ContextManager.Config.Strategy = newStrategy;

            var strategyDesc = newStrategy switch
            {
                ContextStrategy.SlidingWindow => "Sliding Window (последние N сообщений)",
                ContextStrategy.StickyFacts => "Sticky Facts (факты + последние N сообщений)",
                ContextStrategy.Branching => "Branching (ветвление диалога)",
                _ => "Неизвестная",
            };
            PrintGreen($"✅ Стратегия изменена на: {strategyDesc}");
        }
        Console.WriteLine();
    }

    private async Task ExecuteRecentAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], out var recentCount))
        {
            PrintRed("❌ Формат: /context recent <N>. Пример: /context recent 15");
            Console.WriteLine();
            return;
        }
        if (recentCount < 1)
        {
            PrintRed("❌ Recent должно быть >= 1");
            Console.WriteLine();
            return;
        }
        ctx.Agent.ContextManager.Config.RecentMessageCount = recentCount;
        PrintGreen($"✅ Recent сообщений установлен: {recentCount}");
        Console.WriteLine();
    }

    private async Task ExecuteIntervalAsync(string[] parts, CommandContext ctx)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], out var interval))
        {
            PrintRed("❌ Формат: /context interval <N>. Пример: /context interval 15");
            Console.WriteLine();
            return;
        }
        if (interval < 1)
        {
            PrintRed("❌ Interval должно быть >= 1");
            Console.WriteLine();
            return;
        }
        ctx.Agent.ContextManager.Config.SummaryInterval = interval;
        PrintGreen($"✅ Интервал summary установлен: каждые {interval} сообщений");
        Console.WriteLine();
    }

    private async Task ExecuteReportAsync(CommandContext ctx)
    {
        PrintYellow("📦 Статус управления контекстом:");
        var cmReport = ctx.Agent.ContextManager;
        var cfgReport = cmReport.Config;
        Console.WriteLine($"   Стратегия: {cfgReport.Strategy}");
        Console.WriteLine($"   Recent: {cfgReport.RecentMessageCount}, Interval: {cfgReport.SummaryInterval}");
        Console.WriteLine($"   Summary блоков: {cmReport.SummaryCount}");
        Console.WriteLine($"   Всего сообщений: {cmReport.TotalHistoryCount}");
        Console.WriteLine();

        if (ctx.Agent.Metrics.ContextComparison.ComparisonCount > 0)
        {
            PrintGreen("📊 Метрики сжатия:");
            Console.WriteLine(ctx.Agent.Metrics.ContextComparison.GetReport());
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("   Пока нет данных (отправьте несколько запросов)");
            Console.ResetColor();
        }
        Console.WriteLine();
    }

    private void ResetContext(CommandContext ctx)
    {
        ctx.Agent.ContextManager.Config.Enabled = false;
        ctx.Agent.ContextManager.Config.RecentMessageCount = 10;
        ctx.Agent.ContextManager.Config.SummaryInterval = 10;
        ctx.Agent.ContextManager.Config.MaxSummaries = 20;
        ctx.Agent.ContextManager.Config.MaxContextTokens = 0;
        ctx.Agent.ContextManager.Config.Strategy = ContextStrategy.SlidingWindow;
        ctx.Agent.ContextManager.SetStrategy(ContextStrategy.SlidingWindow);
        ctx.Agent.Metrics.ContextCompressionEnabled = false;
        ctx.Agent.ContextManager.Clear();
        ctx.Agent.Metrics.ContextComparison.Reset();
    }
}
