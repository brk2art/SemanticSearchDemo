using Microsoft.Extensions.AI;

// What one question produces: the answer, what search returned, and the exact prompt sent.
public record AssistantReply(string Answer, List<(Product Product, float Score)> Results, string Prompt);

// The read path: retrieve, build the prompt, generate.
public class ShopAssistant(IProductIndex index, IChatClient chat)
{
    public const string Refusal = "I don't have that information.";

    private const string SystemPrompt = """
        You are a shop assistant for an outdoor equipment store.
        Answer using only the text under "Products".
        Each product has a name and a short description. Nothing else is known about it.
        If the question asks for a detail that is not written there, say you don't have
        that information. Never guess, estimate or assume.
        Do not mention products that are not in the list.
        Keep answers to one or two sentences.
        """;

    public async Task<AssistantReply> AskAsync(string question, int top = 3)
    {
        // 1. Retrieve
        var results = await index.SearchAsync(question, top);

        // 2. Build the prompt. The rule is repeated after the question because
        //    small models follow the most recent instruction best.
        var context = string.Join("\n", results.Select(r => $"- {r.Product.Name}: {r.Product.Description}"));

        var userMessage = $"""
            Products:
            {context}

            Question: {question}

            Answer only from the product text above. If it does not state the answer,
            reply exactly: {Refusal}
            """;

        List<ChatMessage> messages =
        [
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userMessage),
        ];

        // 3. Generate. Temperature 0 makes the answer repeatable.
        var response = await chat.GetResponseAsync(messages, new ChatOptions { Temperature = 0 });

        return new AssistantReply(response.Text.Trim(), results, $"{SystemPrompt}\n{userMessage}");
    }
}
