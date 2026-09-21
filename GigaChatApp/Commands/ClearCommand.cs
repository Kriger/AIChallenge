namespace GigaChatApp.Commands;

public sealed class ClearCommand : CommandHandler
{
    public override string Name => "clear";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        ctx.Agent.ClearHistory();
        PrintGray("🗑  История очищена.");
        Console.WriteLine();
        return true;
    }
}
