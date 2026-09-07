using System.Net.Http.Headers;

namespace GigaChatApp.Services;

/// <summary>
/// Прослойка для выполнения HTTP-запросов к GigaChat API.
/// Отвечает только за отправку запроса и десериализацию ответа.
/// </summary>
public class ChatClient
{
    private readonly HttpClient _httpClient;

    public ChatClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Отправляет список сообщений в API и возвращает сырой ответ.
    /// </summary>
    public async Task<Models.RawApiResponse> SendCompletionAsync(
        string model,
        List<Models.ApiMessage> messages,
        int? maxTokens,
        double? temperature,
        string[]? stopSequences,
        string systemMessage,
        string accessToken
    )
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var messagesList = new List<object>();

        if (!string.IsNullOrEmpty(systemMessage))
        {
            messagesList.Add(new { Role = "system", Content = systemMessage });
        }
        messagesList.AddRange(messages.Select(m => new { m.Role, m.Content }));

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messagesList,
            ["stream"] = false,
            ["repetition_penalty"] = 1,
        };

        if (maxTokens.HasValue && maxTokens.Value > 0)
        {
            requestBody["max_tokens"] = maxTokens.Value;
        }

        if (stopSequences is { Length: > 0 })
        {
            requestBody["stop"] = stopSequences;
        }

        if (temperature.HasValue)
        {
            requestBody["temperature"] = temperature.Value;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", requestBody);

        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Ошибка API GigaChat ({response.StatusCode}): {errorContent}"
            );
        }

        var parsed = await response.Content.ReadFromJsonAsync<CompletionResponse>();

        if (parsed?.Choices is null || parsed.Choices.Count == 0)
        {
            throw new InvalidOperationException("Не удалось получить ответ от GigaChat.");
        }

        return new Models.RawApiResponse
        {
            Content = parsed.Choices[0].Message?.Content ?? "Пустой ответ",
            Duration = stopwatch.Elapsed,
            Usage = parsed.Usage,
        };
    }
}
