using Microsoft.Extensions.AI;
using OllamaSharp;
using System.Numerics.Tensors;

IEmbeddingGenerator<string, Embedding<float>> generator = new OllamaApiClient(new Uri("http://localhost:11434/"), "all-minilm");

// One comparison by hand, then the same with the library.
var a = await generator.GenerateVectorAsync("Lightweight waterproof jacket with a hood");
var b = await generator.GenerateVectorAsync("something to keep me dry when it rains");

Console.WriteLine($"Dimensions:       {a.Length}");
Console.WriteLine($"Hand-written:     {VectorMath.Cosine(a.Span, b.Span):F4}");
Console.WriteLine($"TensorPrimitives: {TensorPrimitives.CosineSimilarity(a.Span, b.Span):F4}");

Product[] products =
[
    new(1, "Rain jacket", "Lightweight waterproof jacket with a hood"),
    new(2, "Waterproof boots", "Hiking boots that keep feet dry in wet weather"),
    new(3, "Tent", "Two-person tent for camping trips"),
    new(4, "Sleeping bag", "Warm sleeping bag for cold nights"),
    new(5, "Headlamp", "Bright LED light worn on the head"),
    new(6, "Camping stove", "Portable gas stove for cooking outdoors"),
    new(7, "Water bottle", "Insulated bottle that keeps drinks cold"),
];



var index = new ProductIndex(generator);
Console.WriteLine($"\nIndexing {products.Length} products...");
await index.AddRangeAsync(products);

Console.WriteLine("Type a search and press Enter. Leave it empty to quit.");
while (true)
{
    Console.Write("\n> ");
    var query = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(query))
        break;

    foreach (var (product, score) in await index.SearchAsync(query, top: 3))
        Console.WriteLine($"  {score:F3}  {product.Name} - {product.Description}");
}