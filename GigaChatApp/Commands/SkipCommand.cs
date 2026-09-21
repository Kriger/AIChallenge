namespace GigaChatApp.Commands;

public sealed class SkipCommand : CommandHandler
{
    public override string Name => "skip";

    public override async Task<bool> ExecuteAsync(string[] parts, CommandContext ctx)
    {
        var reqStatus = ctx.TaskStateMachine.GetRequirementsStatus();
        if (reqStatus is not null && !reqStatus.IsComplete)
        {
            PrintYellow("⏭ Пропускаю вопросы. Перехожу к планированию...");
            Console.WriteLine();
            var fsmResult = await ctx.TaskStateMachine.RunAutoAsync("");
            Console.WriteLine();
            Console.ForegroundColor = fsmResult.IsSuccess ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine($"✅ {fsmResult.Message}");
            Console.ResetColor();
            Console.WriteLine();
        }
        else
        {
            PrintYellow("⏭ FSM не ожидает ответа. Пропуск не нужен.");
            Console.WriteLine();
        }
        return true;
    }
}
