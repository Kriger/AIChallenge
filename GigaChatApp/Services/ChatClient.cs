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
        string accessToken,
        List<Models.ApiMessage>? systemMessages = null
    )
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var messagesList = new List<object>();

        // СЛИВАЕМ все system-сообщения в одно — API GigaChat требует одно system-сообщение
        var mergedSystemMessage = new StringBuilder();

        // Сначала summary из ContextManager
        if (systemMessages is { Count: > 0 })
        {
            foreach (var msg in systemMessages)
            {
                mergedSystemMessage.AppendLine(msg.Content);
            }
        }

        // Затем основное системное сообщение
        if (!string.IsNullOrEmpty(systemMessage))
        {
            mergedSystemMessage.AppendLine(systemMessage);
        }

        // Добавляем ОДНО слитое system-сообщение (если есть контент)
        var mergedContent = mergedSystemMessage.ToString().Trim();
        if (!string.IsNullOrEmpty(mergedContent))
        {
            messagesList.Add(new { Role = "system", Content = mergedContent });
        }

        // Затем user/assistant сообщения
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
