using AIChallenge.Models;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AIChallenge.Core;

/// <summary>
/// Клиент для локальной LLM через Ollama API.
/// Работает без облачных сервисов и OAuth.
/// </summary>
public sealed class LocalLlmClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly LocalLlmConfig _config;

    public LocalLlmClient(LocalLlmConfig config)
    {
        _config = config;
        _http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            BaseAddress = new Uri(config.Url.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
        };
    }

    /// <summary>
    /// Проверяет, доступен ли Ollama по указанному URL.
    /// </summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _http.GetAsync("/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Получает список доступных локальных моделей.
    /// </summary>
    public async Task<List<string>> ListModelsAsync()
    {
        try
        {
            var response = await _http.GetAsync("/api/tags");
            response.EnsureSuccessStatusCode();

            var raw = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(raw);
            var models = doc.RootElement.GetProperty("models");

            var result = new List<string>();
            foreach (var model in models.EnumerateArray())
            {
                var name = model.GetProperty("name").GetString();
                if (!string.IsNullOrEmpty(name))
                    result.Add(name);
            }
            return result;
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Отправляет запрос к локальной LLM и возвращает ответ.
    /// </summary>
    public async Task<(string Content, TokenUsage? Usage)> ChatAsync(
        string? systemPrompt,
        string userPrompt,
        double? temperature = null,
        int? maxTokens = null)
    {
        var messages = new List<Dictionary<string, string>>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new Dictionary<string, string>
            {
                ["role"] = "system",
                ["content"] = systemPrompt,
            });
        }

        messages.Add(new Dictionary<string, string>
        {
            ["role"] = "user",
            ["content"] = userPrompt,
        });

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = _config.Model,
            ["messages"] = messages,
            ["stream"] = false,
            ["options"] = new Dictionary<string, object>
            {
                ["temperature"] = temperature ?? _config.Temperature,
                ["num_predict"] = ResolveMaxTokens(maxTokens),
            },
        };

        var jsonOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody, jsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await _http.PostAsync("/api/chat", jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"Ollama API error ({response.StatusCode}): {err}");
        }

        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        var answer = root.GetProperty("message").GetProperty("content").GetString()
            ?? throw new Exception("Ollama returned empty response");

        TokenUsage? usage = null;
        if (root.TryGetProperty("eval_count", out var evalCount) && root.TryGetProperty("prompt_eval_count", out var promptEvalCount))
        {
            usage = new TokenUsage
            {
                CompletionTokens = evalCount.GetInt32(),
                PromptTokens = promptEvalCount.GetInt32(),
                TotalTokens = evalCount.GetInt32() + promptEvalCount.GetInt32(),
            };
        }

        return (answer, usage);
    }

    /// <summary>
    /// Отправляет запрос с историей диалога.
    /// </summary>
    public async Task<string> ChatAsync(
        List<(string Role, string Content)> history,
        double? temperature = null,
        int? maxTokens = null)
    {
        var messages = history
            .Where(h => !string.IsNullOrEmpty(h.Content))
            .Select(h => new Dictionary<string, string>
            {
                ["role"] = h.Role,
                ["content"] = h.Content,
            })
            .ToList();

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = _config.Model,
            ["messages"] = messages,
            ["stream"] = false,
            ["options"] = new Dictionary<string, object>
            {
                ["temperature"] = temperature ?? _config.Temperature,
                ["num_predict"] = ResolveMaxTokens(maxTokens),
            },
        };

        var jsonOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody, jsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await _http.PostAsync("/api/chat", jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"Ollama API error ({response.StatusCode}): {err}");
        }

        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        return root.GetProperty("message").GetProperty("content").GetString()
            ?? throw new Exception("Ollama returned empty response");
    }

    private int ResolveMaxTokens(int? maxTokens)
    {
        if (maxTokens.HasValue) return maxTokens.Value;
        if (_config.MaxTokens > 0) return _config.MaxTokens;
        return -1; // -1 = no limit in Ollama
    }

    public void Dispose()
    {
        _http?.Dispose();
    }
}
