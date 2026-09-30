using System.Net.Http.Json;

namespace AIChallenge.DocumentIndexing.Embeddings;

/// <summary>
/// Провайдер эмбеддингов через Ollama API.
/// Работает локально, требует запущенного Ollama.
/// Модель и URL задаются при инициализации через конфигурацию.
/// </summary>
public class OllamaEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private int _dimension;

    public string ProviderName => "ollama";

    /// Размерность зависит от модели. Задается при первой генерации эмбеддинга.
    public int Dimension => _dimension;

    /// <param name="baseAddress">URL Ollama сервера</param>
    /// <param name="model">Имя модели (mxbai-embed-large, 1024 dim, 80+ языков)</param>
    public OllamaEmbeddingProvider(string baseAddress, string model)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromMinutes(5) };
        _model = model;
        // Размерность определяется по первой успешной генерации
        _dimension = 0;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty", nameof(text));

        var request = new { model = _model, input = text.Trim() };

        var response = await _httpClient.PostAsJsonAsync("/api/embed", request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Ollama Embeddings API error ({response.StatusCode}): {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();

        if (result == null || result.Embeddings == null)
            throw new InvalidOperationException($"Не удалось получить эмбеддинг от Ollama");

        var embeddingsArr = result.Embeddings.Value;
        if (embeddingsArr.ValueKind != System.Text.Json.JsonValueKind.Array || embeddingsArr.GetArrayLength() == 0)
            throw new InvalidOperationException($"Пустой ответ от Ollama");

        var embeddingArr = embeddingsArr[0];
        var count = embeddingArr.GetArrayLength();
        var vector = new float[count];
        for (var i = 0; i < count; i++)
            vector[i] = embeddingArr[i].GetSingle();

        // Запоминаем размерность
        if (_dimension == 0) _dimension = count;

        return vector;
    }

    public async Task GenerateAndSaveEmbeddingsAsync(
        IEnumerable<string> texts,
        string embeddingsFilePath,
        IProgress<int>? progress = null)
    {
        var textArray = texts.ToArray();
        var count = textArray.Length;
        if (count == 0) return;

        // Сначала генерируем один эмбеддинг, чтобы узнать размерность
        var sampleVector = await GenerateEmbeddingAsync(textArray[0]);
        var dimension = sampleVector.Length;

        var directory = Path.GetDirectoryName(embeddingsFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        using var fs = new FileStream(embeddingsFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        bw.Write(1); // version
        bw.Write(dimension);
        bw.Write(count);

        // Записываем первый эмбеддинг (мы его уже сгенерировали)
        foreach (var v in sampleVector) bw.Write(v);

        for (var i = 1; i < count; i++)
        {
            try
            {
                var vector = await GenerateEmbeddingAsync(textArray[i]);
                foreach (var value in vector) bw.Write(value);
                progress?.Report(i + 1);
            }
            catch (Exception ex)
            {
                for (var j = 0; j < dimension; j++) bw.Write(0f);
                Console.WriteLine($"  ⚠️  Ошибка эмбеддинга чанка {i + 1}/{count}: {ex.Message}");
            }
        }

        bw.Flush();
    }

    public async IAsyncEnumerable<float[]> LoadEmbeddingsAsync(string embeddingsFilePath)
    {
        if (!File.Exists(embeddingsFilePath))
            throw new FileNotFoundException($"Embeddings file not found: {embeddingsFilePath}");

        using var fs = new FileStream(embeddingsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);

        var version = br.ReadInt32();
        var dimension = br.ReadInt32();
        var count = br.ReadInt32();

        for (var i = 0; i < count; i++)
        {
            var vector = new float[dimension];
            for (var j = 0; j < dimension; j++)
                vector[j] = br.ReadSingle();

            yield return vector;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    /// <summary>
    /// Ответ Ollama API: { "embeddings": [[...], ...] }
    /// </summary>
    private class OllamaEmbeddingResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("embeddings")]
        public System.Text.Json.JsonElement? Embeddings { get; set; }
    }
}
