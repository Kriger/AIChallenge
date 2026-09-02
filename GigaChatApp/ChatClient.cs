using System.Net.Http.Headers;

namespace GigaChatApp;

/// <summary>
/// Клиент для отправки запросов в GigaChat API.
/// </summary>
public class ChatClient
{
    private readonly HttpClient _httpClient;
    private readonly GigaChatConfig _config;
    private readonly AuthClient _authClient;

    private readonly List<ChatMessage> _messages = new();

    public ChatClient(HttpClient httpClient, GigaChatConfig config, AuthClient authClient)
    {
        _httpClient = httpClient;
        _config = config;
        _authClient = authClient;
    }

    public async Task<string> SendMessageAsync(string userMessage)
    {
        _messages.Add(new ChatMessage { Role = "user", Content = userMessage });

        var token = await _authClient.GetAccessTokenAsync();

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var requestBody = new
        {
            model = "GigaChat-2",
            messages = _messages.Select(m => new { m.Role, m.Content }),
            stream = false,
            repetition_penalty = 1,
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/v1/chat/completions",
            requestBody
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Ошибка API GigaChat ({response.StatusCode}): {errorContent}"
            );
        }

        var chatResponse = await response.Content.ReadFromJsonAsync<CompletionResponse>();

        if (chatResponse?.Choices is null || chatResponse.Choices.Count == 0)
        {
            throw new InvalidOperationException("Не удалось получить ответ от GigaChat.");
        }

        var answer = chatResponse.Choices[0].Message?.Content ?? "Пустой ответ";

        _messages.Add(new ChatMessage { Role = "assistant", Content = answer });

        return answer;
    }

    public void ClearHistory()
    {
        _messages.Clear();
    }

    private class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    private class CompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ChoiceItem>? Choices { get; set; }
    }

    private class ChoiceItem
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }
}
