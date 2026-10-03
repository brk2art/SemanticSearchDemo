using Microsoft.Extensions.AI;
using System.Numerics.Tensors;

public class ProductIndex(IEmbeddingGenerator<string, Embedding<float>> generator) : IProductIndex
{
    private readonly List<(Product Product, ReadOnlyMemory<float> Vector)> _items = [];

    // Indexing: embed all products in one call and keep the vectors.
    public async Task AddRangeAsync(IReadOnlyList<Product> products)
    {
        var texts = products.Select(p => $"{p.Name}. {p.Description}");
        var embeddings = await generator.GenerateAsync(texts);

        for (int i = 0; i < products.Count; i++)
            _items.Add((products[i], embeddings[i].Vector));
    }

    // Searching: embed the query with the same model, score it against every
    // stored vector, and return the closest products.
    public async Task<List<(Product Product, float Score)>> SearchAsync(string query, int top = 3)
    {
        var queryVector = await generator.GenerateVectorAsync(query);

        return _items
            .Select(item => (item.Product, Score: TensorPrimitives.CosineSimilarity(queryVector.Span, item.Vector.Span)))
            .OrderByDescending(result => result.Score)
            .Take(top)
            .ToList();
    }
}