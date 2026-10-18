using System.ComponentModel;
using Microsoft.Extensions.AI;

// What the customer is asking for, as data. The model fills this in; code acts on it.
// The [Description] texts become part of the JSON schema the model must follow.
public record ShopRequest(
    [property: Description("""
        What the customer wants. One of:
        price = the price of one or more named products, including a total or a quantity;
        stock = whether a named product can be bought now, or how many the shop has;
        compare = which of the named products is cheaper or more expensive;
        most_expensive = the most expensive product of a kind;
        cheapest = the cheapest product of a kind;
        other = anything else.
        """)] string Kind,
    [property: Description("Product codes from the product list that the question names. Empty if it names none.")] string[] Codes,
    [property: Description("A kind of product, such as tent. Only for most_expensive and cheapest. Empty when the question names specific products.")] string Category,
    [property: Description("How many items the customer wants, if the question says. Otherwise 1.")] int Quantity);

// Turns a question into a ShopRequest using structured output.
public class RequestExtractor(IChatClient chat)
{
    // The same schema GetResponseAsync<T> sends as the format, as text the model can read.
    public static readonly string SchemaText = AIJsonUtilities.CreateJsonSchema(typeof(ShopRequest)).ToString();

    private const string Prompt = """
        You read questions to an outdoor shop and describe what the customer is asking for.
        Use only product codes that appear in the product list. Never invent a code.
        If the question asks about a product that is not in the list, leave codes empty.
        """;

    public async Task<(ShopRequest? Request, string Json)> ExtractAsync(string question, string productList)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, $"{Prompt}\nReply with JSON that follows this schema:\n{SchemaText}"),
            new(ChatRole.User, $"Products:\n{productList}\n\nQuestion: {question}"),
        ];

        // GetResponseAsync<T> also sends the schema as the format, so the reply is always valid JSON.
        var response = await chat.GetResponseAsync<ShopRequest>(messages, new ChatOptions { Temperature = 0 });

        return response.TryGetResult(out var request) ? (request, response.Text) : (null, response.Text);
    }
}