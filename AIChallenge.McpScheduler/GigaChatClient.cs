using System.Net.Http.Headers;
using System.Text.Encodings.Web;

namespace AIChallenge.McpScheduler;

/// <summary>
/// Лёгкий клиент для GigaChat API.
/// </summary>
public sealed class GigaChatClient : IDisposable
{
    private readonly string _clientId;
    private readonly string _clientSecret;
    private string? _accessToken;
    private DateTime _tokenExpiresAt;

    public GigaChatClient(string clientId, string clientSecret)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    private async Task<string> GetAccessTokenAsync()
    {
        if (_accessToken is not null && DateTime.UtcNow < _tokenExpiresAt)
            return _accessToken;

        using var http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = TimeSpan.FromMinutes(1),
            BaseAddress = new Uri("https://ngw.devices.sberbank.ru:9443/"),
        };

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_clientId}:{_clientSecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        http.DefaultRequestHeaders.Add("RqUID", Guid.NewGuid().ToString());

        var response = await http.PostAsync("api/v2/oauth", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("scope", "GIGACHAT_API_PERS")
        }));

        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        _accessToken = root.GetProperty("access_token").GetString();
        var expiresAt = root.GetProperty("expires_at").GetInt64();
        _tokenExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expiresAt).UtcDateTime.AddSeconds(-30);

        return _accessToken!;
    }

    public async Task<string> ChatAsync(string model, string systemPrompt, string userPrompt, double temperature = 0.3, int maxTokens = 2000)
    {
        var token = await GetAccessTokenAsync();

        using var http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = TimeSpan.FromMinutes(5),
            BaseAddress = new Uri("https://api.giga.chat"),
        };

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = false,
            repetition_penalty = 1,
            temperature,
            max_tokens = maxTokens
        };

        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var content = new StringContent(JsonSerializer.Serialize(requestBody, options), Encoding.UTF8, "application/json");

        var response = await http.PostAsync("/v1/chat/completions", content);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"GigaChat API error ({response.StatusCode}): {err}");
        }

        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        return root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;
    }

    public void Dispose()
    {
    }
}
