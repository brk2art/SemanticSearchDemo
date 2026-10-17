using Microsoft.Extensions.AI;

// Decides, before answering, whether a question needs live price or stock data.
// If not, the assistant is given no tools, so it cannot call one it doesn't need.
public class ToolRouter(IChatClient chat)
{
    private const string Prompt = """
        Decide whether a question to an outdoor shop needs live price or stock data.
        Reply with exactly one word: yes or no.
        yes: the question asks what something costs, which is cheaper, whether it is in stock, or how many the shop has.
        no: everything else, such as what a product is for, which product suits a need, or shop policies.
        """;

    public async Task<bool> NeedsToolsAsync(string question)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, Prompt),
            new(ChatRole.User, question),
        ];

        var response = await chat.GetResponseAsync(messages, new ChatOptions { Temperature = 0 });
        var needs = response.Text.Trim().StartsWith("yes", StringComparison.OrdinalIgnoreCase);

        Console.WriteLine($"  [route] {(needs ? "tools" : "no tools")}");
        return needs;
    }
}