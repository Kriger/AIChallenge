using AIChallenge.DocumentIndexing.Models;

namespace AIChallenge.DocumentIndexing.Indexing;

/// <summary>
/// Хранилище индекса для сохранения и загрузки.
/// Поддерживает FAISS (через бинарный формат) и JSON.
/// </summary>
public interface IIndexStorage
{
    /// <summary>
    /// Название хранилища.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Сохраняет индекс в хранилище.
    /// </summary>
    /// <param name="index">Индекс для сохранения.</param>
    /// <param name="embeddings">Матрица эмбеддингов.</param>
    /// <param name="path">Путь для сохранения.</param>
    Task SaveAsync(DocumentIndex index, float[][] embeddings, string path);

    /// <summary>
    /// Загружает индекс из хранилища.
    /// </summary>
    /// <param name="path">Путь для загрузки.</param>
    /// <returns>Загруженный индекс и матрица эмбеддингов.</returns>
    Task<(DocumentIndex Index, float[][] Embeddings)> LoadAsync(string path);

    /// <summary>
    /// Выполняет поиск по запросу в индексе.
    /// </summary>
    /// <param name="queryEmbedding">Эмбеддинг запроса.</param>
    /// <param name="topK">Количество ближайших результатов.</param>
    /// <param name="embeddings">Матрица эмбеддингов.</param>
    /// <returns>Список индексов ближайших соседей и расстояний.</returns>
    (int[] indices, float[] distances) Search(float[] queryEmbedding, int topK, float[][] embeddings);
}
