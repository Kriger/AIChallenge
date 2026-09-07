namespace GigaChatApp.Infrastructure;

/// <summary>
/// Логгер агента — запись событий с уровнями и цветовой разметкой.
/// </summary>
public class AgentLogger
{
    private LogLevel _minLevel;

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

    public void Log(LogLevel level, string message)
    {
        if (level < _minLevel)
            return;

        Console.WriteLine();
        
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var color = level switch
        {
            LogLevel.Debug => ConsoleColor.DarkGray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[{timestamp}] [AGENT LOG]");
        Console.ResetColor();

        Console.ForegroundColor = color;
        Console.WriteLine($"   {message}");
        Console.ResetColor();
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Info, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);
}
