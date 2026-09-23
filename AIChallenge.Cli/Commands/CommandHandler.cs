using AIChallenge.Core;
using AIChallenge.Models;
using AIChallenge.Core.Infrastructure;

namespace AIChallenge.Cli.Commands;

/// <summary>
/// Shared context passed to all command handlers.
/// </summary>
public sealed class CommandContext
{
    public ChatAgent Agent { get; }
    public GigaChatConfig Config { get; }
    public TaskStateMachine TaskStateMachine { get; }
    public AgentLogger Logger { get; }
    public RequestCache Cache { get; }
    public MemoryManager MemoryManager { get; }

    public CommandContext(ChatAgent agent, GigaChatConfig config, TaskStateMachine taskStateMachine, AgentLogger logger, RequestCache cache, MemoryManager memoryManager)
    {
        Agent = agent;
        Config = config;
        TaskStateMachine = taskStateMachine;
        Logger = logger;
        Cache = cache;
        MemoryManager = memoryManager;
    }
}

/// <summary>
/// Base class for command handlers.
/// </summary>
public abstract class CommandHandler
{
    /// <summary>
    /// Command name without leading slash (e.g., "status", "model").
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Execute the command handler.
    /// </summary>
    /// <param name="parts">Parsed input parts (parts[0] is the command itself).</param>
    /// <param name="ctx">Shared context with agent, config, etc.</param>
    /// <returns>true if the command was handled, false otherwise.</returns>
    public abstract Task<bool> ExecuteAsync(string[] parts, CommandContext ctx);
}

/// <summary>
/// Registry that maps command names to their handlers.
/// </summary>
public static class CommandRegistry
{
    private static readonly Dictionary<string, CommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(CommandHandler handler)
    {
        _handlers[handler.Name] = handler;
    }

    public static CommandHandler? GetHandler(string command)
    {
        return _handlers.TryGetValue(command, out var handler) ? handler : null;
    }

    public static IReadOnlyCollection<string> Commands => _handlers.Keys;
}
