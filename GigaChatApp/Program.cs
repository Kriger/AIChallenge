using GigaChatApp;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("═══════════════════════════════════════════════");
Console.WriteLine("   GigaChat CLI Client");
Console.WriteLine("   Общение с LLM через GigaChat API");
Console.WriteLine("═══════════════════════════════════════════════");
Console.WriteLine();

var builder = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

var configuration = builder.Build();
var config = new GigaChatConfig();
configuration.GetSection("GigaChat").Bind(config);

if (string.IsNullOrEmpty(config.ClientId))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("❌ Ошибка: не загружена конфигурация.");
    Console.WriteLine("   Проверьте файл appsettings.json.");
    Console.WriteLine();
    Console.WriteLine("   1. Зарегистрируйтесь: https://developer.sber.ru/portal/dev/products/gigachat");
    Console.WriteLine("   2. Создайте приложение и получите Client ID и Client Secret");
    Console.WriteLine("   3. Внесите их в appsettings.json");
    Console.ResetColor();
    return;
}

Console.WriteLine("✅ Конфигурация загружена");
Console.WriteLine();

// Выбор модели
Console.WriteLine("📦 Доступные модели:");
foreach (var kvp in AvailableModels.Models)
{
    var marker = kvp.Value == config.Model ? " ▶" : "";
    Console.WriteLine($"   {kvp.Key}. {kvp.Value}{marker}");
}
Console.WriteLine();
Console.WriteLine("   Для смены модели во время работы используйте команду /model");
Console.WriteLine();
Console.WriteLine();
Console.WriteLine("📖 Команды: /status, /model, /system <текст>, /maxtokens <число>, /stop <seq1,seq2>, /temp <0-2>");
Console.WriteLine("   Очистка: /clear | Выход: quit / exit / q");
Console.WriteLine("   По умолчанию ограничений нет — задайте через команды выше.");
Console.WriteLine();

var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
};

using var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri("https://api.giga.chat"),
    Timeout = TimeSpan.FromMinutes(5),
};

var authClient = new AuthClient(httpClient, config);
var chatClient = new ChatClient(httpClient, config, authClient);

Console.WriteLine("🔄 Инициализация подключения к GigaChat...");

try
{
    var token = await authClient.GetAccessTokenAsync();
    Console.WriteLine("✅ Подключение успешно!");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"❌ Ошибка аутентификации: {ex.Message}");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("   Проверьте ваши учётные данные в appsettings.json.");
    return;
}

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("✏️  Вы: ");
    Console.ResetColor();

    var input = Console.ReadLine()?.Trim();

    if (input is null or "quit" or "exit" or "выход" or "q")
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("👋 До свидания!");
        Console.ResetColor();
        break;
    }

    if (input is "clear" or "очисти" or "/clear")
    {
        chatClient.ClearHistory();
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("🗑  История очищена.");
        Console.ResetColor();
        Console.WriteLine();
        continue;
    }

    // Команды конфигурации
    if (input.StartsWith("/"))
    {
        var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
        var command = parts[0].ToLowerInvariant();

        switch (command)
        {
            case "/status":
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("📋 Текущие настройки:");
                Console.ResetColor();
                Console.WriteLine($"   Модель: {config.Model}");
                Console.WriteLine($"   Temperature: {config.Temperature ?? (object)"(не задано)"}");
                Console.WriteLine($"   MaxTokens: {config.MaxTokens}");
                Console.WriteLine($"   StopSequences: [{string.Join(", ", config.StopSequences.Select(s => $"\"{s}\""))}]");
                Console.WriteLine($"   SystemMessage: {config.SystemMessage}");
                Console.WriteLine();
                continue;

            case "/model":
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("📦 Доступные модели:");
                Console.ResetColor();
                foreach (var kvp in AvailableModels.Models)
                {
                    var marker = kvp.Value == config.Model ? " ▶" : "";
                    Console.WriteLine($"   {kvp.Key}. {kvp.Value}{marker}");
                }
                Console.WriteLine();
                Console.Write("   Введите номер или название: ");
                Console.ResetColor();

                var modelInputCmd = Console.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(modelInputCmd))
                {
                    if (int.TryParse(modelInputCmd, out var modelNumber) && AvailableModels.Models.ContainsKey(modelNumber))
                    {
                        config.Model = AvailableModels.Models[modelNumber];
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Модель изменена на: {config.Model}");
                        Console.ResetColor();
                    }
                    else if (AvailableModels.Models.ContainsValue(modelInputCmd))
                    {
                        config.Model = modelInputCmd;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Модель изменена на: {config.Model}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Модель '{modelInputCmd}' не найдена.");
                        Console.ResetColor();
                    }
                }
                Console.WriteLine();
                continue;

            case "/system":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите новое системное сообщение: /system <текст>");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                config.SystemMessage = parts[1];
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ SystemMessage обновлён.");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            case "/maxtokens":
                if (parts.Length < 2 || !int.TryParse(parts[1], out var maxTokens))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите число: /maxtokens <число>");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                config.MaxTokens = maxTokens;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ MaxTokens установлен в {maxTokens}.");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            case "/stop":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите стоп-последовательности через запятую: /stop <seq1,seq2,...>");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                config.StopSequences = parts[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ StopSequences обновлены: [{string.Join(", ", config.StopSequences.Select(s => $"\"{s}\""))}]");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            case "/temp":
            case "/temperature":
                if (parts.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите число: /temp <0-2>. Пример: /temp 0.7");
                    Console.WriteLine("   /temp clear — сбросить ограничение.");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                if (parts[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    config.Temperature = null;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✅ Temperature сброшен.");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                double temp;
                if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out temp))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Укажите число: /temp <0-2>. Пример: /temp 0.7");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                if (temp < 0 || temp > 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("❌ Temperature должно быть в диапазоне 0–2.");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }
                config.Temperature = temp;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ Temperature установлен в {temp}.");
                Console.ResetColor();
                Console.WriteLine();
                continue;

            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Неизвестная команда: {command}. Доступны: /status, /model, /system, /maxtokens, /stop, /temp");
                Console.ResetColor();
                Console.WriteLine();
                continue;
        }
    }

    if (string.IsNullOrEmpty(input))
    {
        continue;
    }

    try
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("⏳ Думает...");
        Console.ResetColor();

        var response = await chatClient.SendMessageAsync(input);

        var duration = response.Duration.TotalSeconds < 1
            ? $"{response.Duration.TotalMilliseconds:F0} мс"
            : $"{response.Duration.TotalSeconds:F1} с";

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("🤖 GigaChat:");
        Console.ResetColor();
        Console.WriteLine($"   {response.Answer}");
        Console.WriteLine();

        // Статистика
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"   ⏱ {duration}");
        if (response.Usage is not null)
        {
            var u = response.Usage;
            Console.Write($"  |  📊 Токены: {u.PromptTokens} в → {u.CompletionTokens} out → {u.TotalTokens} всего");
        }
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ Ошибка: {ex.Message}");
        Console.ResetColor();
        Console.WriteLine();
    }
}
