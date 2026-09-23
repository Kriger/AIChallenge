namespace AIChallenge.Models;

/// <summary>
/// Общий интерфейс для стратегий, хранящих факты.
/// </summary>
public interface IFactStorage
{
    IReadOnlyDictionary<string, string> Facts { get; }
    void SaveFact(string key, string value);
    bool DeleteFact(string key);
}
