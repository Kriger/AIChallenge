using AIChallenge.McpPipeline;
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
    case "standard":
        await RunStandardPipelineAsync(configuration);
        break;

    case "llm":
        await RunLlmPipelineAsync(configuration);
        break;

    case "search-save":
        await RunSearchSavePipelineAsync(configuration);
        break;

    case "custom":
        await RunCustomPipelineAsync(configuration);
        break;

    case "config":
        PrintConfig(configuration);
        break;

    case "help":
    default:
        PrintHelp();
        break;
}

static async Task RunStandardPipelineAsync(IConfiguration configuration)
{
    Console.WriteLine("🚀 MCP Pipeline — стандартный режим (search → summarize → saveToFile)");
    Console.WriteLine();

    var log = new Action<string>(msg => Console.WriteLine($"  ℹ️  {msg}"));
    var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory");
    var outputPath = Path.Combine(outputDir, "pipeline_report.txt");

    using var executor = new PipelineExecutor(
        configuration,
        AppDomain.CurrentDomain.BaseDirectory,
        log
    );

    var result = await executor.RunStandardPipelineAsync(outputPath, "stats");

    PrintResult(result);
}

static async Task RunLlmPipelineAsync(IConfiguration configuration)
{
    Console.WriteLine("🚀 MCP Pipeline — LLM-режим (search → summarize[llm] → saveToFile)");
    Console.WriteLine();

    var log = new Action<string>(msg => Console.WriteLine($"  ℹ️  {msg}"));

    // Проверяем, есть ли GigaChat-конфиг
    var gigaSection = configuration.GetSection("GigaChat");
    LlmSummaryService? llmService = null;

    if (gigaSection.Exists())
    {
        var clientId = gigaSection["ClientId"];
        var clientSecret = gigaSection["ClientSecret"];
        var model = gigaSection["Model"] ?? "GigaChat-2";
        double temperature = 0.3;
        if (double.TryParse(gigaSection["Temperature"], out var parsedTemp))
            temperature = parsedTemp;
        int maxTokens = 2000;
        if (int.TryParse(gigaSection["MaxTokens"], out var parsedMt))
            maxTokens = parsedMt;

        llmService = new LlmSummaryService(clientId!, clientSecret!, model, temperature, maxTokens, log);
        Console.WriteLine($"🧠 LLM включён: {model} (температура: {temperature})");
    }
    else
    {
        Console.WriteLine("ℹ️  GigaChat не настроен — LLM-режим недоступен");
        Console.WriteLine("   Используйте 'standard' или 'search-save'");
        return;
    }

    var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory");
    var outputPath = Path.Combine(outputDir, "pipeline_llm_report.md");

    using var executor = new PipelineExecutor(
        configuration,
        AppDomain.CurrentDomain.BaseDirectory,
        log,
        llmService
    );

    var result = await executor.RunLlmPipelineAsync(outputPath);

    PrintResult(result);
}

static async Task RunSearchSavePipelineAsync(IConfiguration configuration)
{
    Console.WriteLine("🚀 MCP Pipeline — поиск и сохранение (search → saveToFile)");
    Console.WriteLine();

    var log = new Action<string>(msg => Console.WriteLine($"  ℹ️  {msg}"));
    var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory");
    var outputPath = Path.Combine(outputDir, "pipeline_tasks.json");

    using var executor = new PipelineExecutor(
        configuration,
        AppDomain.CurrentDomain.BaseDirectory,
        log
    );

    var result = await executor.RunSearchSavePipelineAsync(outputPath);

    PrintResult(result);
}

static async Task RunCustomPipelineAsync(IConfiguration configuration)
{
    Console.WriteLine("🚀 MCP Pipeline — пользовательский режим");
    Console.WriteLine();

    var log = new Action<string>(msg => Console.WriteLine($"  ℹ️  {msg}"));

    // Запрашиваем параметры
    Console.WriteLine("Введите шаги пайплайна через запятую (например: search,summarize,saveToFile):");
    var stepsInput = Console.ReadLine()?.ToLowerInvariant() ?? "search,summarize,saveToFile";
    var steps = stepsInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    Console.WriteLine("Введите путь для сохранения (или Enter для авто-имени):");
    var outputPath = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(outputPath))
    {
        var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory");
        outputPath = Path.Combine(outputDir, $"pipeline_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
    }

    Console.WriteLine("Режим суммаризации (stats/diff/llm, Enter=stats):");
    var mode = Console.ReadLine()?.ToLowerInvariant() ?? "stats";

    using var executor = new PipelineExecutor(
        configuration,
        AppDomain.CurrentDomain.BaseDirectory,
        log
    );

    var pipelineSteps = new List<(string ToolName, Dictionary<string, object?> Params)>();

    foreach (var step in steps)
    {
        if (step == "search")
        {
            pipelineSteps.Add(("search", new Dictionary<string, object?>()));
        }
        else if (step == "summarize")
        {
            pipelineSteps.Add(("summarize", new Dictionary<string, object?> { ["mode"] = mode }));
        }
        else if (step == "saveToFile")
        {
            pipelineSteps.Add(("saveToFile", new Dictionary<string, object?>
            {
                ["path"] = outputPath,
                ["format"] = "text"
            }));
        }
        else
        {
            Console.WriteLine($"   ⚠️  Неизвестный шаг: {step}");
        }
    }

    if (pipelineSteps.Count == 0)
    {
        Console.WriteLine("   ❌ Нет валидных шагов");
        return;
    }

    var pipelineStepsTyped = pipelineSteps.Select(s =>
        new PipelineStep(s.ToolName, s.Params)
    ).ToList();

    var result = await executor.ExecuteAsync(pipelineStepsTyped);

    PrintResult(result);
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
    foreach (var kvp in result.StepResults)
    {
        var preview = kvp.Value.Length > 200 ? kvp.Value[..200] + "..." : kvp.Value;
        Console.WriteLine($"   [{kvp.Key}]: {preview.Replace("\n", "\n   ")}");
    }

    Console.WriteLine();
    Console.WriteLine("Итоговый отчёт:");
    Console.WriteLine(result.Output);
    Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
}

static void PrintConfig(IConfiguration configuration)
{
    Console.WriteLine("⚙️  MCP Pipeline — настройки");
    Console.WriteLine();

    var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    Console.WriteLine($"   Версия: {version}");
    Console.WriteLine($"   Платформа: .NET {Environment.Version}");
    Console.WriteLine($"   ОС: {Environment.OSVersion}");
    Console.WriteLine();

    var todoSection = configuration.GetSection("Mcp:Servers:Todo");
    Console.WriteLine("📋 TodoMCP:");
    if (todoSection.Exists())
    {
        var baseUrl = todoSection["BaseUrl"] ?? "не задан";
        var username = todoSection["Username"] ?? "не задан";
        Console.WriteLine($"   BaseUrl: {baseUrl}");
        Console.WriteLine($"   Username: {username}");
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
    {
        var model = gigaSection["Model"] ?? "не задан";
        Console.WriteLine($"   Модель: {model}");
    }
    else
    {
        Console.WriteLine("   ⚠️  Не настроен (LLM-режим недоступен)");
    }
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

static void PrintHelp()
{
    Console.WriteLine("🚀 MCP Pipeline CLI");
    Console.WriteLine();
    Console.WriteLine("Использование:");
    Console.WriteLine("  dotnet run -- standard      — стандартный пайплайн (search → summarize → saveToFile)");
    Console.WriteLine("  dotnet run -- llm           — пайплайн с LLM-суммаризацией");
    Console.WriteLine("  dotnet run -- search-save   — только поиск и сохранение");
    Console.WriteLine("  dotnet run -- custom        — пользовательский пайплайн");
    Console.WriteLine("  dotnet run -- config        — показать настройки");
    Console.WriteLine("  dotnet run -- help          — показать справку");
    Console.WriteLine();
    Console.WriteLine("Структура пайплайна:");
    Console.WriteLine("  1️⃣  search     — получение данных из MCP-сервера (JSON)");
    Console.WriteLine("  2️⃣  summarize  — обработка и генерация отчёта");
    Console.WriteLine("  3️⃣  saveToFile — сохранение результата в файл");
    Console.WriteLine();
    Console.WriteLine("Примеры:");
    Console.WriteLine("  dotnet run -- standard");
    Console.WriteLine("  dotnet run -- llm");
    Console.WriteLine("  dotnet run -- search-save");
    Console.WriteLine("  dotnet run -- custom");
    Console.WriteLine("  dotnet run -- config");
}
