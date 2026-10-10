using Microsoft.Extensions.AI;
using OllamaSharp;
using System.Numerics.Tensors;
using Npgsql;
using SemanticSearchDemo;

IEmbeddingGenerator<string, Embedding<float>> generator = new OllamaApiClient(new Uri("http://localhost:11434/"), "nomic-embed-text");
IChatClient chat = new OllamaApiClient(new Uri("http://localhost:11434/"), "llama3.2");

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

// "eval" runs the golden set and exits instead of starting the chat loop.
if (args.Contains("eval"))
{
    await RetrievalEval.RunAsync(index);
    return;
}

// Pass "debug" to print the exact prompt sent to the model.
var debug = args.Contains("debug");

// The standing instructions. Sent with every request.
const string SystemPrompt = """
    You are a shop assistant for an outdoor equipment store.
    Answer using only the products listed under "Products".
    If the answer is not in that list, say you don't have that information.
    Do not mention products that are not in the list.
    Keep answers to one or two sentences.
    """;

Console.WriteLine("Type a search and press Enter. Leave it empty to quit.");
while (true)
{
    Console.Write("\n> ");
    var query = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(query))
        break;

    // 1. Retrieve: find the products closest in meaning to the question.
    var results = await index.SearchAsync(query, top: 3);
    foreach (var (product, score) in results)
        Console.WriteLine($"  {score:F3}  {product.Name} - {product.Description}");

    // 2. Build the prompt: retrieved products become text the model can read.
    //    The rule is repeated after the question because small models follow
    //    the most recent instruction best.
    var context = string.Join("\n", results.Select(r => $"- {r.Product.Name}: {r.Product.Description}"));

    var userMessage = $"""
        Products:
        {context}

        Question: {query}

        Answer only from the product text above. If it does not state the answer,
        reply exactly: I don't have that information.
        """;

    List<ChatMessage> messages =
    [
        new(ChatRole.System, SystemPrompt),
        new(ChatRole.User, userMessage),
    ];

    if (debug)
    {
        Console.WriteLine("\n--- prompt sent ---");
        Console.WriteLine(SystemPrompt);
        Console.WriteLine(userMessage);
        Console.WriteLine("-------------------");
    }

    // 3. Generate: temperature 0 makes the answer repeatable.
    var response = await chat.GetResponseAsync(messages, new ChatOptions { Temperature = 0 });
    Console.WriteLine($"\nAnswer: {response.Text}");
}