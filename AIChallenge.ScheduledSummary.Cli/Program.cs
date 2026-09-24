using AIChallenge.McpScheduler;
using AIChallenge.ScheduledSummary.Cli;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;

var builder = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

var configuration = builder.Build();

var rawArgs = Environment.GetCommandLineArgs();
var command = rawArgs.Length > 1 ? rawArgs[1].ToLowerInvariant() : "help";

switch (command)
{
    case "snapshot":
        await RunSnapshotAsync(configuration);
        break;

    case "report":
        await RunReportAsync(configuration);
        break;

    case "cron":
        await RunCronAsync(configuration);
        break;

    case "config":
        PrintConfig(configuration);
        break;

    case "llm-summary":
        await RunLlmSummaryAsync(configuration);
        break;

    case "clear":
        RunClearAsync();
        break;

    case "help":
    default:
        PrintHelp();
        break;
}

static async Task RunSnapshotAsync(IConfiguration configuration)
{
    Console.WriteLine("📸 Scheduled Summary — создание снимка");
    Console.WriteLine();

    var todoSection = configuration.GetSection("Mcp:Servers:Todo");
    if (!todoSection.Exists())
    {
        Console.WriteLine("❌ Конфигурация TodoMCP не найдена в appsettings.json");
        Console.WriteLine("   Добавьте раздел Mcp:Servers:Todo");
        return;
    }

    var baseUrl = todoSection["BaseUrl"] ?? "https://localhost:7162";
    var username = todoSection["Username"];
    var password = todoSection["Password"];

    Console.WriteLine($"🔌 Подключение к TodoMCP: {baseUrl}...");
    using var client = new TodoMcpClient(baseUrl, username, password);
    await client.ConnectAsync();
    Console.WriteLine("✅ Подключено");

    Console.WriteLine("📋 Загрузка задач...");
    var rawTasks = await client.ListTodoItemsAsync();

    var scheduler = new ScheduledSummaryService(
        AppDomain.CurrentDomain.BaseDirectory,
        msg => Console.WriteLine($"  ℹ️  {msg}"),
        () => Task.FromResult(rawTasks)
    );

    var result = await scheduler.TakeSnapshotAsync();
    Console.WriteLine();
    Console.WriteLine(result);
}

static async Task RunReportAsync(IConfiguration configuration)
{
    Console.WriteLine("📊 Scheduled Summary — последний отчёт");
    Console.WriteLine();

    var todoSection = configuration.GetSection("Mcp:Servers:Todo");
    if (!todoSection.Exists())
    {
        Console.WriteLine("❌ Конфигурация TodoMCP не найдена в appsettings.json");
        return;
    }

    var baseUrl = todoSection["BaseUrl"] ?? "https://localhost:7162";
    var username = todoSection["Username"];
    var password = todoSection["Password"];

    Console.WriteLine($"🔌 Подключение к TodoMCP: {baseUrl}...");
    using var client = new TodoMcpClient(baseUrl, username, password);
    await client.ConnectAsync();
    Console.WriteLine("✅ Подключено");

    Console.WriteLine("📋 Загрузка задач...");
    var rawTasks = await client.ListTodoItemsAsync();

    var scheduler = new ScheduledSummaryService(
        AppDomain.CurrentDomain.BaseDirectory,
        msg => Console.WriteLine($"  ℹ️  {msg}"),
        () => Task.FromResult(rawTasks)
    );

    var result = await scheduler.TakeSnapshotAsync();
    Console.WriteLine();
    Console.WriteLine(result);
}

static async Task RunCronAsync(IConfiguration configuration)
{
    Console.WriteLine("⏰ Scheduled Summary — cron-режим");
    Console.WriteLine();

    var interval = 3600;
    if (Environment.GetCommandLineArgs().Length > 2 &&
        int.TryParse(Environment.GetCommandLineArgs()[2], out var parsedInterval))
    {
        interval = parsedInterval;
    }

    Console.WriteLine($"Интервал: {interval} секунд");
    Console.WriteLine("Нажмите Ctrl+C для остановки");
    Console.WriteLine();

    var todoSection = configuration.GetSection("Mcp:Servers:Todo");
    if (!todoSection.Exists())
    {
        Console.WriteLine("❌ Конфигурация TodoMCP не найдена в appsettings.json");
        return;
    }

    var baseUrl = todoSection["BaseUrl"] ?? "https://localhost:7162";
    var username = todoSection["Username"];
    var password = todoSection["Password"];

    // Проверяем, есть ли GigaChat-конфиг
    var gigaSection = configuration.GetSection("GigaChat");
    LlmSummaryService? llmService = null;

    if (gigaSection.Exists())
    {
        var clientId = gigaSection["ClientId"];
        var clientSecret = gigaSection["ClientSecret"];
        var model = gigaSection["Model"] ?? "GigaChat-2";
        var temperature = gigaSection.GetValue<double?>("Temperature") ?? 0.3;
        int maxTokens = 2000;
        if (int.TryParse(gigaSection["MaxTokens"], out var parsedMt))
            maxTokens = parsedMt;

        llmService = new LlmSummaryService(clientId!, clientSecret!, model, temperature, maxTokens,
            msg => Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] {msg}"));
        Console.WriteLine($"🧠 LLM включён: {model} (температура: {temperature})");
    }
    else
    {
        Console.WriteLine("ℹ️  GigaChat не настроен — будут использоваться сухие diff-отчёты");
        Console.WriteLine("   Добавьте раздел GigaChat в appsettings.json для умных отчётов");
    }
    Console.WriteLine();

    var scheduler = new ScheduledSummaryService(
        AppDomain.CurrentDomain.BaseDirectory,
        msg => Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] {msg}"),
        async () =>
        {
            using var c = new TodoMcpClient(baseUrl, username, password);
            await c.ConnectAsync();
            return await c.ListTodoItemsAsync();
        },
        llmService
    );

    using var _ = llmService;

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (s, e) =>
    {
        Console.WriteLine("\n\n⏹ Остановка...");
        cts.Cancel();
        e.Cancel = true;
    };

    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            Console.WriteLine();
            Console.WriteLine($"━━━ Снимок от {DateTime.Now:yyyy-MM-dd HH:mm:ss} ━━━");
            var result = await scheduler.TakeSnapshotAsync();
            Console.WriteLine(result);

            try
            {
                await Task.Delay(interval * 1000, cts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine();
        Console.WriteLine("✅ Цикл остановлен");
    }
}

static async Task RunLlmSummaryAsync(IConfiguration configuration)
{
    Console.WriteLine("🤖 LLM Summary — умный отчёт через GigaChat");
    Console.WriteLine();

    var gigaSection = configuration.GetSection("GigaChat");
    if (!gigaSection.Exists())
    {
        Console.WriteLine("❌ Конфигурация GigaChat не найдена в appsettings.json");
        Console.WriteLine("   Добавьте раздел GigaChat с ClientId, ClientSecret, Model");
        return;
    }

    var clientId = gigaSection["ClientId"];
    var clientSecret = gigaSection["ClientSecret"];
    var model = gigaSection["Model"] ?? "GigaChat-2";
    var temperature = gigaSection.GetValue<double?>("Temperature") ?? 0.3;
    int maxTokens;
    if (int.TryParse(gigaSection["MaxTokens"], out var parsedMt))
        maxTokens = parsedMt;
    else
        maxTokens = 2000;

    Console.WriteLine($"🧠 Модель: {model}");
    Console.WriteLine($"🌡️  Температура: {temperature}");
    Console.WriteLine();

    var todoSection = configuration.GetSection("Mcp:Servers:Todo");
    if (!todoSection.Exists())
    {
        Console.WriteLine("❌ Конфигурация TodoMCP не найдена в appsettings.json");
        return;
    }

    var baseUrl = todoSection["BaseUrl"] ?? "https://localhost:7162";
    var username = todoSection["Username"];
    var password = todoSection["Password"];

    Console.WriteLine($"🔌 Подключение к TodoMCP: {baseUrl}...");
    using var todoClient = new TodoMcpClient(baseUrl, username, password);
    await todoClient.ConnectAsync();
    Console.WriteLine("✅ Подключено");

    Console.WriteLine("📋 Загрузка задач...");
    var rawTasks = await todoClient.ListTodoItemsAsync();

    var log = new Action<string>(msg => Console.WriteLine($"  ℹ️  {msg}"));
    using var llmService = new LlmSummaryService(clientId!, clientSecret!, model, temperature, maxTokens, log);

    Console.WriteLine();
    Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    var summary = await llmService.GenerateSummaryAsync(rawTasks);
    Console.WriteLine(summary);
    Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
}

static void PrintConfig(IConfiguration configuration)
{
    Console.WriteLine("⚙️  Scheduled Summary — настройки");
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
        Console.WriteLine("   ❌ Не настроен (добавьте Mcp:Servers:Todo в appsettings.json)");
    }
    Console.WriteLine();

    var memoryDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory");
    var snapshotFile = Path.Combine(memoryDir, "scheduled_summary_snapshots.json");
    Console.WriteLine("💾 Хранилище:");
    Console.WriteLine($"   Каталог: {memoryDir}");
    Console.WriteLine($"   Файл снимков: {snapshotFile}");
    if (File.Exists(snapshotFile))
    {
        var fileInfo = new FileInfo(snapshotFile);
        Console.WriteLine($"   Размер файла: {fileInfo.Length:N0} байт");
        Console.WriteLine($"   Последнее изменение: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");

        try
        {
            var json = File.ReadAllText(snapshotFile);
            using var doc = JsonDocument.Parse(json);
            var count = doc.RootElement.GetArrayLength();
            Console.WriteLine($"   Сохранённых снимков: {count}");
        }
        catch
        {
            Console.WriteLine("   ⚠️  Не удалось прочитать файл");
        }
    }
    else
    {
        Console.WriteLine("   Снимков пока нет");
    }
    Console.WriteLine();
}

static void PrintHelp()
{
    Console.WriteLine("📊 Scheduled Summary CLI");
    Console.WriteLine();
    Console.WriteLine("Использование:");
    Console.WriteLine("  dotnet run -- snapshot    — сделать снимок и показать отчёт");
    Console.WriteLine("  dotnet run -- report      — сделать снимок и показать отчёт");
    Console.WriteLine("  dotnet run -- cron [сек]  — запустить в фоновом режиме (по умолч. 3600с)");
    Console.WriteLine("  dotnet run -- config      — показать настройки");
    Console.WriteLine("  dotnet run -- llm-summary — умный отчёт через GigaChat LLM");
    Console.WriteLine("  dotnet run -- clear       — очистить снимки и начать с нуля");
    Console.WriteLine("  dotnet run -- help        — показать справку");
    Console.WriteLine();
    Console.WriteLine("Примеры:");
    Console.WriteLine("  dotnet run -- snapshot");
    Console.WriteLine("  dotnet run -- cron 1800   — интервал 30 минут");
    Console.WriteLine("  dotnet run -- config      — вывод настроек");
    Console.WriteLine("  dotnet run -- llm-summary — умный отчёт через LLM");
    Console.WriteLine("  dotnet run -- clear       — очистить снимки и начать с нуля");
    Console.WriteLine();
    Console.WriteLine("💡 В cron-режиме автоматически используется LLM, если настроен GigaChat");
}

static void RunClearAsync()
{
    Console.WriteLine("🗑️  Scheduled Summary — очистка памяти");
    Console.WriteLine();

    var memoryDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "memory");
    var files = new[]
    {
        "scheduled_summary_snapshots.json",
        "scheduled_summary_last_llm_summary.json"
    };

    if (!Directory.Exists(memoryDir))
    {
        Console.WriteLine("ℹ️  Каталог memory/ не существует, нечего очищать");
        return;
    }

    var cleared = 0;
    foreach (var file in files)
    {
        var fullPath = Path.Combine(memoryDir, file);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            Console.WriteLine($"   🗑️  Удалён: {file}");
            cleared++;
        }
    }

    if (cleared == 0)
    {
        Console.WriteLine("ℹ️  Файлов для очистки не найдено");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine($"✅ Очищено {cleared} файл(ов). Можно начинать отслеживание с нуля.");
    }
}
