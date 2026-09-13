namespace GigaChatApp.Infrastructure;

/// <summary>
/// Логгер агента — запись событий с уровнями и цветовой разметкой.
/// Поддерживает буферизацию для вывода после ответа бота.
/// </summary>
public class AgentLogger
{
    private LogLevel _minLevel;
    private readonly List<string> _bufferedMessages = new();

    public AgentLogger(LogLevel minLevel = LogLevel.Info)
    {
        _minLevel = minLevel;
    }

    /// <summary>
    /// Установить минимальный уровень логирования.
    /// </summary>
    public void SetMinLevel(LogLevel level)
    {
        _minLevel = level;
    }

    /// <summary>
    /// Получить и очистить буферизованные сообщения.
    /// </summary>
    public List<string> FlushBuffer()
    {
        var messages = _bufferedMessages.ToList();
        _bufferedMessages.Clear();
        return messages;
    }

    /// <summary>
    /// Вывести буферизованные сообщения в консоль с цветами.
    /// </summary>
    public static void PrintBufferedMessages(IEnumerable<string> messages)
    {
        foreach (var msg in messages)
        {
            var parts = msg.Split('\n', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(parts[0]);
                Console.ResetColor();
                Console.WriteLine(parts[1]);
            }
            else
            {
                Console.WriteLine(msg);
            }
        }
    }

    public void Log(LogLevel level, string message)
    {
        if (level < _minLevel)
            return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var color = level switch
        {
            LogLevel.Debug => ConsoleColor.DarkGray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };

        var formatted = $"{timestamp} [AGENT LOG]";
        if (level >= LogLevel.Warning)
        {
            formatted += $" [{level}]";
        }
        formatted += $"\n   {message}";

        // Буферизуем для вывода после ответа
        _bufferedMessages.Add(formatted);
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);
}
