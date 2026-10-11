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

var products = Catalog.Products;

// Enrichment adds generated use cases to the text that gets embedded.
// On by default; "no-enrich" turns it off, for comparison.
if (!args.Contains("no-enrich"))
{
    Console.WriteLine("\nEnriching products...");
    products = await Enricher.AddUsesAsync(products, chat);
}

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

// Reranking wraps the index so a chat model reorders the top 10.
// On by default; "no-rerank" turns it off, for comparison.
if (!args.Contains("no-rerank"))
{
    index = new RerankingIndex(index, chat);
    Console.WriteLine("Reranking the top 10 with the chat model.");
}

var assistant = new ShopAssistant(index, chat);

// "eval" runs the retrieval golden set (about a second) and exits.
if (args.Contains("eval"))
{
    await RetrievalEval.RunAsync(index);
    return;
}

// "answers" runs the answer golden set (several minutes) and exits.
if (args.Contains("answers"))
{
    await AnswerEval.RunAsync(assistant);
    return;
}

if (args.Contains("judge"))
{
    await JudgeEval.RunAsync(assistant, chat);
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