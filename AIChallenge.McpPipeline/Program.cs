using AIChallenge.McpPipeline;
using AIChallenge.McpPipeline.Tools;
using AIChallenge.McpScheduler;
using Microsoft.Extensions.Configuration;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

var builder = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

var configuration = builder.Build();

var rawArgs = Environment.GetCommandLineArgs();
var command = rawArgs.Length > 1 ? rawArgs[1].ToLowerInvariant() : "help";

switch (command)
{
    case "standard":  await RunStandardPipelineAsync(configuration); break;
    case "llm":       await RunLlmPipelineAsync(configuration); break;
    case "full-llm":  await RunFullLlmPipelineAsync(configuration); break;
    case "search-save": await RunSearchSavePipelineAsync(configuration); break;
    case "custom":    await RunCustomPipelineAsync(configuration); break;
    case "config":    PrintConfig(configuration); break;
    case "clear":     RunClearAsync(); break;
    case "help":
    default:          PrintHelp(); break;
}

// ─── Pipeline runners ──────────────────────────────────────────────────

static async Task RunStandardPipelineAsync(IConfiguration config)
{
    Console.WriteLine("🚀 MCP Pipeline — стандартный режим (search → enrich → summarize → saveToFile)\n");

    var log = CreateLog();
    var outputPath = GetOutputPath(config, "pipeline_report.txt");

    using var executor = new PipelineExecutor(config, AppDomain.CurrentDomain.BaseDirectory, log);
    var result = await executor.RunFullPipelineAsync(outputPath, "stats");
    PrintResult(result);
}

static async Task RunLlmPipelineAsync(IConfiguration config)
{
    Console.WriteLine("🚀 MCP Pipeline — LLM-режим (search → summarize[llm] → saveToFile)\n");

    var log = CreateLog();
    if (!TryCreateLlmService(config, log, out var llmService)) return;

    var outputPath = GetOutputPath(config, "pipeline_llm_report.md");

    using var executor = new PipelineExecutor(config, AppDomain.CurrentDomain.BaseDirectory, log, llmService);
    var result = await executor.RunLlmPipelineAsync(outputPath);
    PrintResult(result);
}

static async Task RunFullLlmPipelineAsync(IConfiguration config)
{
    Console.WriteLine("🚀 MCP Pipeline — полный LLM-режим (search → enrich → summarize[llm] → saveToFile)\n");

    var log = CreateLog();
    if (!TryCreateLlmService(config, log, out var llmService)) return;

    var outputPath = GetOutputPath(config, "pipeline_full_llm_report.md");

    using var executor = new PipelineExecutor(config, AppDomain.CurrentDomain.BaseDirectory, log, llmService);
    var result = await executor.RunFullLlmPipelineAsync(outputPath);
    PrintResult(result);
}

static async Task RunSearchSavePipelineAsync(IConfiguration config)
{
    Console.WriteLine("🚀 MCP Pipeline — поиск и сохранение (search → saveToFile)\n");

    var log = CreateLog();
    var outputPath = GetOutputPath(config, "pipeline_tasks.json");

    using var executor = new PipelineExecutor(config, AppDomain.CurrentDomain.BaseDirectory, log);
    var result = await executor.RunSearchSavePipelineAsync(outputPath);
    PrintResult(result);
}

static async Task RunCustomPipelineAsync(IConfiguration config)
{
    Console.WriteLine("🚀 MCP Pipeline — пользовательский режим\n");

    var log = CreateLog();
    Console.WriteLine("Введите шаги пайплайна через запятую (например: search,summarize,saveToFile):");
    var stepsInput = Console.ReadLine()?.ToLowerInvariant() ?? "search,summarize,saveToFile";
    var steps = stepsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    Console.WriteLine("Введите путь для сохранения (или Enter для авто-имени):");
    var outputPath = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(outputPath))
        outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory", $"pipeline_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

    Console.WriteLine("Режим суммаризации (stats/diff/llm, Enter=stats):");
    var mode = Console.ReadLine()?.ToLowerInvariant() ?? "stats";

    using var executor = new PipelineExecutor(config, AppDomain.CurrentDomain.BaseDirectory, log);

    var pipelineSteps = new List<PipelineStep>();
    foreach (var step in steps)
    {
        var stepDef = step switch
        {
            "search" => new PipelineStep("search", new Dictionary<string, object?>()),
            "enrich" => new PipelineStep("enrich", new Dictionary<string, object?> { ["mode"] = "both" }),
            "summarize" => new PipelineStep("summarize", new Dictionary<string, object?> { ["mode"] = mode }),
            "saveToFile" or "savetofile" => new PipelineStep("saveToFile", new Dictionary<string, object?> { ["path"] = outputPath, ["format"] = "json" }),
            _ => null
        };

        if (stepDef == null)
        {
            Console.WriteLine($"   ⚠️  Неизвестный шаг: {step}");
            continue;
        }

        pipelineSteps.Add(stepDef);
    }

    if (pipelineSteps.Count == 0)
    {
        Console.WriteLine("   ❌ Нет валидных шагов");
        return;
    }

    var result = await executor.ExecuteAsync(pipelineSteps);
    PrintResult(result);
}

// ─── Helpers ───────────────────────────────────────────────────────────

static Action<string> CreateLog() => msg => Console.WriteLine($"  ℹ️  {msg}");

static bool TryCreateLlmService(IConfiguration config, Action<string> log, out LlmSummaryService? llmService)
{
    llmService = null;
    if (!LlmConfigHelper.TryParse(config, log, out var llmConfig, out llmService))
    {
        Console.WriteLine("ℹ️  GigaChat не настроен — LLM-режим недоступен");
        Console.WriteLine("   Добавьте раздел GigaChat в appsettings.json для умных отчётов");
        return false;
    }

    Console.WriteLine($"🧠 LLM включён: {llmConfig!.Model} (температура: {llmConfig.Temperature})");
    return true;
}

static string GetOutputPath(IConfiguration config, string defaultFileName)
{
    var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory");
    return Path.Combine(outputDir, defaultFileName);
}

static void PrintResult(PipelineResult result)
{
    Console.WriteLine();
    Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

    if (result.Success)
    {
        Console.WriteLine("✅ Пайплайн выполнен успешно!");
        Console.WriteLine($"   Время: {result.Duration.TotalMilliseconds:F0}мс");
        Console.WriteLine($"   Шагов: {result.StepResults.Count}");
    }
    else
    {
        Console.WriteLine($"❌ Ошибка: {result.Error}");
    }

    Console.WriteLine();
    Console.WriteLine("Результаты шагов:");
    Console.WriteLine();

    var stepIndex = 0;
    foreach (var kvp in result.StepResults)
    {
        if (stepIndex > 0)
            Console.WriteLine("   ──────────────────────────────────────────────");

        Console.WriteLine($"   📌 Шаг {stepIndex + 1}: {kvp.Key}");
        Console.WriteLine();

        var lines = kvp.Value.Split('\n');
        foreach (var line in lines)
            Console.WriteLine($"     {line}");

        stepIndex++;
    }

    Console.WriteLine();
    Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    Console.WriteLine();
    Console.WriteLine("Итоговый отчёт:");
    Console.WriteLine();
    Console.WriteLine(result.Output);
    Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
}

static void PrintConfig(IConfiguration configuration)
{
    Console.WriteLine("⚙️  MCP Pipeline — настройки\n");

    var version = typeof(PipelineExecutor).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    Console.WriteLine($"   Версия: {version}");
    Console.WriteLine($"   Платформа: .NET {Environment.Version}");
    Console.WriteLine($"   ОС: {Environment.OSVersion}");
    Console.WriteLine();

    var todoSection = configuration.GetSection("Mcp:Servers:Todo");
    Console.WriteLine("📋 TodoMCP:");
    if (todoSection.Exists())
    {
        Console.WriteLine($"   BaseUrl: {todoSection["BaseUrl"] ?? "не задан"}");
        Console.WriteLine($"   Username: {todoSection["Username"] ?? "не задан"}");
        Console.WriteLine($"   Password: {(string.IsNullOrEmpty(todoSection["Password"]) ? "не задан" : "******")}");
    }
    else
    {
        Console.WriteLine("   ⚠️  Не настроен (используются тестовые данные)");
    }
    Console.WriteLine();

    var gigaSection = configuration.GetSection("GigaChat");
    Console.WriteLine("🧠 GigaChat LLM:");
    if (gigaSection.Exists())
        Console.WriteLine($"   Модель: {gigaSection["Model"] ?? "не задан"}");
    else
        Console.WriteLine("   ⚠️  Не настроен (LLM-режим недоступен)");
    Console.WriteLine();

    var memoryDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory");
    Console.WriteLine("💾 Хранилище:");
    Console.WriteLine($"   Каталог: {memoryDir}");
    if (Directory.Exists(memoryDir))
    {
        var files = Directory.GetFiles(memoryDir);
        Console.WriteLine($"   Файлов: {files.Length}");
        foreach (var f in files)
        {
            var info = new FileInfo(f);
            Console.WriteLine($"     • {info.Name} ({info.Length:N0} байт)");
        }
    }
    else
    {
        Console.WriteLine("   Каталог ещё не создан");
    }
    Console.WriteLine();
}

static void RunClearAsync()
{
    Console.WriteLine("🗑️  MCP Pipeline — очистка памяти\n");

    var memoryDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory");
    if (!Directory.Exists(memoryDir))
    {
        Console.WriteLine("ℹ️  Каталог memory/ не существует, нечего очищать");
        return;
    }

    var files = Directory.GetFiles(memoryDir, "pipeline_*");
    var cleared = 0;
    foreach (var file in files)
    {
        File.Delete(file);
        Console.WriteLine($"   🗑️  Удалён: {Path.GetFileName(file)}");
        cleared++;
    }

    if (cleared == 0)
        Console.WriteLine("ℹ️  Файлов для очистки не найдено");
    else
        Console.WriteLine($"\n✅ Очищено {cleared} файл(ов).");
}

static void PrintHelp()
{
    Console.WriteLine("🚀 MCP Pipeline CLI");
    Console.WriteLine();
    Console.WriteLine("Использование:");
    Console.WriteLine("  dotnet run -- standard      — полный пайплайн (search → enrich → summarize → saveToFile)");
    Console.WriteLine("  dotnet run -- llm           — пайплайн с LLM-суммаризацией");
    Console.WriteLine("  dotnet run -- full-llm      — полный LLM-пайплайн (search → enrich → summarize[llm] → saveToFile)");
    Console.WriteLine("  dotnet run -- search-save   — только поиск и сохранение");
    Console.WriteLine("  dotnet run -- custom        — пользовательский пайплайн");
    Console.WriteLine("  dotnet run -- config        — показать настройки");
    Console.WriteLine("  dotnet run -- clear         — очистить сгенерированные файлы");
    Console.WriteLine("  dotnet run -- help          — показать справку");
    Console.WriteLine();
    Console.WriteLine("Структура пайплайна:");
    Console.WriteLine("  1️⃣  search     — получение данных из MCP-сервера (JSON)");
    Console.WriteLine("  2️⃣  enrich     — приоритизация по срокам + обогащение описаний");
    Console.WriteLine("  3️⃣  summarize  — обработка и генерация отчёта");
    Console.WriteLine("  4️⃣  saveToFile — сохранение результата в файл");
    Console.WriteLine();
    Console.WriteLine("Инструмент enrich:");
    Console.WriteLine("  • Повышает приоритеты задач с близкими дедлайнами");
    Console.WriteLine("  • < 3 дня → Очень высокий (4)");
    Console.WriteLine("  • < 7 дней → Высокий (3)");
    Console.WriteLine("  • Просроченные → Критический (5)");
    Console.WriteLine("  • Генерирует описания через LLM если их нет");
}
