namespace AIChallenge.DocumentIndexing.Embeddings;

/// <summary>
/// Интерфейс для провайдеров эмбеддингов.
/// </summary>
public interface IEmbeddingProvider
{
    string ProviderName { get; }
    int Dimension { get; }
    Task<float[]> GenerateEmbeddingAsync(string text);
    Task GenerateAndSaveEmbeddingsAsync(IEnumerable<string> texts, string embeddingsFilePath, IProgress<int>? progress = null);
    IAsyncEnumerable<float[]> LoadEmbeddingsAsync(string embeddingsFilePath);
}
