namespace GigaChatApp.Commands;

/// <summary>
/// Keywords that trigger the FSM auto-start.
/// </summary>
public static class FsmKeywords
{
    public static string[] All => new[]
    {
        "спроектируй", "спроектируйте",
        "составь", "составьте",
        "разработай", "разработайте",
        "создай", "создайте",
        "спланируй", "спланируйте",
        "реализуй", "реализуйте",
        "подготовь", "подготовьте",
        "напиши", "напишите",
        "сделай", "сделайте",
    };
}
