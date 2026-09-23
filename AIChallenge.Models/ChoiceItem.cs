namespace AIChallenge.Models;

/// <summary>
/// Элемент коллекции choices.
/// </summary>
public class ChoiceItem
{
    [JsonPropertyName("message")]
    public ApiMessage? Message { get; set; }
}
