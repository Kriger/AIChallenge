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

        var messagesList = new List<object>();

        if (!string.IsNullOrEmpty(_config.SystemMessage))
        {
            messagesList.Add(new { Role = "system", Content = _config.SystemMessage });
        }
        messagesList.AddRange(_messages.Select(m => new { m.Role, m.Content }));

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = _config.Model,
            ["messages"] = messagesList,
            ["stream"] = false,
            ["repetition_penalty"] = 1,
        };

        if (_config.MaxTokens > 0)
        {
            requestBody["max_tokens"] = _config.MaxTokens;
        }

        if (_config.StopSequences.Length > 0)
        {
            requestBody["stop"] = _config.StopSequences;
        }

        if (_config.Temperature.HasValue)
        {
            requestBody["temperature"] = _config.Temperature.Value;
        }

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
