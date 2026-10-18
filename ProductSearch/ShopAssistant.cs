using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

// What one question produces: the answer, what search returned, the exact prompt sent,
// and what the guard did ("ok", "rewritten" or "fallback").
public record AssistantReply(string Answer, List<(Product Product, float Score)> Results, string Prompt, string Guard = "ok");

// The read path: retrieve, build the prompt, generate (calling tools if offered), check the answer.
public class ShopAssistant(IProductIndex index, IChatClient chat, IList<AITool>? tools = null, 
    ToolRouter? router = null, RequestExtractor? extractor = null, ShopTools? shopTools = null)
{
    public const string Refusal = "I don't have that information.";
    public const string Fallback = "Sorry, I can't give you a reliable answer to that right now. Please ask a member of staff.";

    private const string SystemPrompt = """
        You are a shop assistant for an outdoor equipment store.
        Answer using only the text under "Products" and the results of the tools you are given.
        Each product has a name and a short description. Nothing else is known about it.
        If the question asks for a detail that is not written there and no tool provides it,
        say you don't have that information. Never guess, estimate or assume.
        Do not mention products that are not in the list.
        Keep answers to one or two sentences.
        """;

    private const string ToolRule =
        "Prices and stock levels come only from the tools. Never state a price or stock level that a tool did not return. " +
        "Use GetPriceAndStock for specific products in the list above: call it once for each different product the question is about. " +
        "A quantity, such as two canisters, needs only one call for that product; then multiply the price. " +
        "Use FindProducts when the question is about all products of a kind (the cheapest, the most expensive, which ones we have) " +
        "or about a product that is not in the list above. " +
        "When the question compares products or asks for a total, state the price of each product, and for a total also give the sum.";

    // A price-like number: digits, a dot, two digits (349.00, 6.95, 56.90).
    private static readonly Regex Price = new(@"\d+\.\d{2}");

    public async Task<AssistantReply> AskAsync(string question, int top = 3)
    {
        // 1. Retrieve
        var results = await index.SearchAsync(question, top);

        // 2. Build the prompt. With a router, tools (and the rule about them) are only offered when needed.
        var context = string.Join("\n", results.Select(r => $"- {r.Product.Name}: {r.Product.Description}"));
        var useTools = tools is not null && (router is null || await router.NeedsToolsAsync(question));
        var system = useTools ? $"{SystemPrompt}\n{ToolRule}" : SystemPrompt;

        var userMessage = $"""
            Products:
            {context}

            Question: {question}

            Answer only from the product text above or from tool results. If neither states
            the answer, reply exactly: {Refusal}
            """;

        // Structured path: the model only describes the request; code fetches data and writes the answer.
        if (useTools && extractor is not null && shopTools is not null)
        {
            var (request, json) = await extractor.ExtractAsync(question, context);
            Console.WriteLine($"  [extract] {json}");

            var allowedCodes = results
                .Select(r => Regex.Match(r.Product.Description, @"Code ([A-Z]{2}-\d{3})").Groups[1].Value)
                .Where(c => c != "")
                .ToHashSet();

            var structured = request is null ? Refusal : AnswerWriter.Write(request, shopTools, allowedCodes);
            return new AssistantReply(structured, results, $"{system}\n{userMessage}");
        }

        List<ChatMessage> messages =
        [
            new(ChatRole.System, system),
            new(ChatRole.User, userMessage),
        ];

        // 3. Generate. With tools, the chat client may call them before answering.
        var options = new ChatOptions { Temperature = 0, Tools = useTools ? tools : null };
        var response = await chat.GetResponseAsync(messages, options);

        // response.Text joins the text of EVERY message in the tool loop, including anything the
        // model wrote next to its tool calls. The customer's answer is only the last assistant message.
        var answer = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text.Trim() ?? "";

        // Every tool result from the loop, read from the response itself.
        var toolResults = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Select(r => r.Result?.ToString() ?? "")
            .ToList();

        // 4. Check before the customer sees it.
        var guard = "ok";
        var problem = FindProblem(answer, toolResults);
        if (problem is not null)
        {
            Console.WriteLine($"  [guard] {problem} -> rewriting from the tool results, no tools offered");
            answer = await RewriteAsync(userMessage, toolResults);
            guard = "rewritten";

            problem = FindProblem(answer, toolResults);
            if (problem is not null)
            {
                Console.WriteLine($"  [guard] still {problem} -> fallback");
                answer = Fallback;
                guard = "fallback";
            }
        }

        return new AssistantReply(answer, results, $"{system}\n{userMessage}", guard);
    }

    // Returns what is wrong with an answer, or null if nothing the code can check is wrong.
    // Public and static so it can be tested on fixed examples, without a model.
    public static string? FindProblem(string answer, List<string> toolResults)
    {
        if (answer.Length == 0)
            return "empty answer";

        if (answer.Contains('{') || answer.Contains("\"name\""))
            return "tool text in the answer";

        var allowed = AllowedPrices(toolResults);
        var invented = Price.Matches(answer)
            .Select(m => m.Value)
            .Where(p => !allowed.Contains(p))
            .Distinct()
            .ToList();

        return invented.Count > 0 ? $"prices no tool returned ({string.Join(", ", invented)})" : null;
    }

    // Prices a correct answer may contain: each returned price, the sum or difference of any two,
    // and 2 to 10 times any one. Any other number was not calculated from real data.
    private static HashSet<string> AllowedPrices(List<string> toolResults)
    {
        var prices = toolResults
            .SelectMany(r => Price.Matches(r).Select(m => decimal.Parse(m.Value, CultureInfo.InvariantCulture)))
            .Distinct()
            .ToList();

        var allowed = new HashSet<decimal>(prices);
        foreach (var a in prices)
        {
            for (var n = 2; n <= 10; n++)
                allowed.Add(a * n);

            foreach (var b in prices)
            {
                allowed.Add(a + b);
                allowed.Add(Math.Abs(a - b));
            }
        }

        return allowed.Select(p => p.ToString("F2", CultureInfo.InvariantCulture)).ToHashSet();
    }

    // Second attempt: the tool results are pasted in as plain text and no tools are offered,
    // so the model can only write sentences, not tool calls.
    private async Task<string> RewriteAsync(string userMessage, List<string> toolResults)
    {
        var facts = toolResults.Count == 0 ? "(none)" : string.Join("\n", toolResults);

        List<ChatMessage> messages =
        [
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, $"""
                {userMessage}

                Tool results:
                {facts}

                Write the answer in one or two plain sentences. Use only the product text and the tool results above.
                """),
        ];

        var response = await chat.GetResponseAsync(messages, new ChatOptions { Temperature = 0 });
        return response.Text.Trim();
    }
}