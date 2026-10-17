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

// "guardtest" checks the answer guard on fixed examples, without any model, then exits.
if (args.Contains("guardtest"))
{
    List<string> toolResults = ["LN-060: price €39.95, 14 in stock.", "FL-055: price €27.95, 20 in stock."];
    (string Answer, bool ShouldPass)[] samples =
    [
        ("The flashlight is cheaper, priced at €27.95.", true),
        ("The lantern costs €39.95 and the flashlight €27.95, together €67.90.", true),
        ("The flashlight is €12.00 cheaper.", true),
        ("The lantern costs 9.99 euros and the flashlight costs 12.99 euros.", false),
        ("{\"name\": \"GetPriceAndStock\", \"parameters\": {\"code\":\"FL-055\"}}", false),
        ("", false),
    ];

    foreach (var (answer, shouldPass) in samples)
    {
        var problem = ShopAssistant.FindProblem(answer, toolResults);
        var verdict = (problem is null) == shouldPass ? "ok  " : "FAIL";
        Console.WriteLine($"{verdict} {problem ?? "passes",-45} {answer}");
    }
    return;
}

// "docs" runs the document pipeline (chapter 5) and exits.
// "fixed" cuts documents every 40 words; without it, each document is one chunk.
// "show-chunks" prints every chunk before the evaluation.
if (args.Contains("docs"))
{
    var folder = Path.Combine(AppContext.BaseDirectory, "docs");
    // Default: Microsoft.Extensions.DataIngestion's HeaderChunker.
    // "own" uses the hand-written section chunker; "whole" or "fixed" for comparison;
    // "no-context" drops the headers from the hand-written one.
    var mode = args.Contains("whole") ? "whole"
             : args.Contains("fixed") ? "fixed"
             : args.Contains("own") ? "sections"
             : "library";

    var chunks = mode switch
    {
        "whole" => Chunker.WholeDocuments(folder),
        "fixed" => Chunker.FixedSize(folder, 40),
        "sections" => Chunker.ByHeadings(folder, withContext: !args.Contains("no-context")),
        _ => await LibraryChunker.ByHeadingsAsync(folder),
    };
    Console.WriteLine($"\nChunker: {mode}");
    var top = mode == "whole" ? 1 : 3;

    if (args.Contains("show-chunks"))
        foreach (var c in chunks)
            Console.WriteLine($"\n[{c.Source} / {c.Section}]\n{c.Text}");

    Console.WriteLine($"\nIndexing {chunks.Count} document chunks...");
    var docIndex = new ChunkIndex(generator);
    await docIndex.AddRangeAsync(chunks);

    await DocEval.RunAsync(new DocAssistant(docIndex, chat), top);
    return;
}

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

// "hybrid" adds keyword search (BM25) and fuses it with vector results.
if (args.Contains("hybrid"))
{
    index = new HybridIndex(index);
    Console.WriteLine("Hybrid search: keyword and vector rankings fused.");
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

// "tools" lets the assistant look up live price and stock; "tooleval" also turns them on.
// "trace" prints every message sent to the model on every call.
ShopTools? shopTools = null;
IList<AITool>? tools = null;
ToolRouter? router = null;
IChatClient answerClient = chat;

if (args.Contains("tools") || args.Contains("tooleval"))
{
    shopTools = new ShopTools();
    tools = 
    [
        AIFunctionFactory.Create(shopTools.GetPriceAndStock),
        AIFunctionFactory.Create(shopTools.FindProducts)
    ];

    // UseFunctionInvocation runs steps 3 and 4 for you: when the model asks for a tool,
    // it calls the method and sends the result back to the model automatically.
    var builder = new ChatClientBuilder(chat).UseFunctionInvocation();

    // Added after UseFunctionInvocation, so it sits closer to the model and sees every round.
    if (args.Contains("trace"))
    {
        builder.Use(async (messages, options, next, ct) =>
        {
            Console.WriteLine($"\n  ==== model call: {messages.Count()} messages ====");
            
            foreach(var m in messages)
            {
                foreach (var c in m.Contents)
                {
                    var text = c switch
                    {
                        TextContent t => t.Text,
                        FunctionCallContent f => $"CALL {f.Name}({string.Join(", ", f.Arguments?.Select(a => $"{a.Key}={a.Value}") ?? [])})",
                        FunctionResultContent r => $"RESULT {r.Result}",
                        _ => c.GetType().Name
                    };
                    Console.WriteLine($"  [{m.Role}] {text}");
                }
            }
            await next(messages, options, ct);
        });
    }

    // Routing is on by default; "no-route" turns it off, for comparison.
    if (!args.Contains("no-route"))
    {
        router = new ToolRouter(chat);
        Console.WriteLine("Routing: tools only when the question needs price or stock.");
    }

    answerClient = builder.Build();
    Console.WriteLine("Tools: GetPriceAndStock, FindProducts");
}

var assistant = new ShopAssistant(index, answerClient, tools, router);

// "tooleval" checks which tools were called, then exits.
if (args.Contains("tooleval"))
{
    await ToolEval.RunAsync(assistant, shopTools!);
    return;
}

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