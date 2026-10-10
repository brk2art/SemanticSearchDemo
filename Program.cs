using Microsoft.Extensions.AI;
using OllamaSharp;
using System.Numerics.Tensors;
using Npgsql;

IEmbeddingGenerator<string, Embedding<float>> generator = new OllamaApiClient(new Uri("http://localhost:11434/"), "nomic-embed-text");
IChatClient chat = new OllamaApiClient(new Uri("http://localhost:11434/"), "llama3.1:8b");

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

IProductIndex index;
if (args.Contains("pg"))
{
    var builder = new NpgsqlDataSourceBuilder("Host=localhost;Username=postgres;Password=postgres;Database=postgres");
    builder.UseVector();
    var db = builder.Build();

    var pgIndex = new PgProductIndex(db, generator);
    await pgIndex.InitializeAsync();
    index = pgIndex;
    Console.WriteLine("\nUsing PostgreSQL with pgvector.");
}
else
{
    index = new ProductIndex(generator);
    Console.WriteLine("\nUsing the in-memory index.");
}

Console.WriteLine($"\nIndexing {products.Length} products...");
await index.AddRangeAsync(products);

var assistant = new ShopAssistant(index, chat);

// "eval" runs the golden set and exits instead of starting the chat loop.
if (args.Contains("eval"))
{
    await RetrievalEval.RunAsync(index);
    await AnswerEval.RunAsync(assistant);
    return;
}

// Pass "debug" to print the exact prompt sent to the model.
var debug = args.Contains("debug");

Console.WriteLine("Type a search and press Enter. Leave it empty to quit.");
while (true)
{
    Console.Write("\n> ");
    var query = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(query))
        break;

    var reply = await assistant.AskAsync(query);

    foreach (var (product, score) in reply.Results)
        Console.WriteLine($"  {score:F3}  {product.Name} - {product.Description}");

    if (debug)
    {
        Console.WriteLine("\n--- prompt sent ---");
        Console.WriteLine(reply.Prompt);
        Console.WriteLine("-------------------");
    }

    Console.WriteLine($"\nAnswer: {reply.Answer}");
}