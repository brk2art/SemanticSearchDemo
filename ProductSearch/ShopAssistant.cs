using Microsoft.Extensions.AI;

// What one question produces: the answer, what search returned, and the exact prompt sent.
public record AssistantReply(string Answer, List<(Product Product, float Score)> Results, string Prompt);

// The read path: retrieve, build the prompt, generate (calling tools if any are given).
public class ShopAssistant(IProductIndex index, IChatClient chat, IList<AITool>? tools = null, ToolRouter? router = null)
{
    public const string Refusal = "I don't have that information.";

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
        "Prices and stock levels come only from the GetPriceAndStock tool. " +
        "Never state a price or stock level that the tool did not return. " +
        "Call GetPriceAndStock only when the question asks about price, cost or stock (how many we have). " +
        "For every other question, answer from the product text and do not call any tool. " +
        "When the question is about more than one product, call GetPriceAndStock once for each of them before you answer.";

    public async Task<AssistantReply> AskAsync(string question, int top = 3)
    {
        // 1. Retrieve
        var results = await index.SearchAsync(question, top);

        // 2. Build the prompt
        var context = string.Join("\n", results.Select(r => $"- {r.Product.Name}: {r.Product.Description}"));
        // With a router, tools (and the rule about them) are only offered when needed.
        var useTools = tools is not null && (router is null || await router.NeedsToolsAsync(question));
        var system = useTools ? $"{SystemPrompt}\n{ToolRule}" : SystemPrompt;

        var userMessage = $"""
            Products:
            {context}

            Question: {question}

            Answer only from the product text above or from tool results. If neither states
            the answer, reply exactly: {Refusal}
            """;

        List<ChatMessage> messages =
        [
            new(ChatRole.System, system),
            new(ChatRole.User, userMessage),
        ];

        // 3. Generate. With tools, the chat client may call them before answering.
        var options = new ChatOptions { Temperature = 0, Tools = useTools ? tools : null };
        var response = await chat.GetResponseAsync(messages, options);

        return new AssistantReply(response.Text.Trim(), results, $"{system}\n{userMessage}");
    }
}
