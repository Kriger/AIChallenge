using System;

namespace AIChallenge.Cli.Commands;

/// <summary>
/// Shared console helper methods for command handlers.
/// </summary>
public static class ConsoleHelpers
{
    public static void PrintRed(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void PrintGreen(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void PrintYellow(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void PrintCyan(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void PrintMagenta(string message)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void PrintGray(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Форматированная строка команды: "   /cmd             — описание".
    /// </summary>
    public static void PrintCmd(string prefix, string cmd, string desc)
    {
        Console.Write($"   /{prefix} {cmd,-45}");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"— {desc}");
        Console.ResetColor();
    }
}