using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

// One line of the agent's shopping list.
public record ListItem(
    [property: Description("The product code, for example SB-310")] string Code,
    [property: Description("How many to buy")] int Quantity);

// Tools that only an agent needs. The agent searches and chooses; code checks, prices and totals.
public class AgentTools(IProductIndex index, ShopTools shop)
{
    private static readonly Regex CodePattern = new(@"Code ([A-Z]{2}-\d{3})");

    // Every code a search has shown the agent in this run. Anything else is refused.
    private readonly HashSet<string> seen = [];

    // The shopping list as code wrote it, once the agent submitted a valid one.
    public string? FinalList { get; private set; }
    // What the evaluation reads: the accepted list and how the run went.
    public List<ListItem> Accepted { get; private set; } = [];
    public int Searches { get; private set; }
    public int Submissions { get; private set; }
    public int Rejections { get; private set; }

    // Called before each new question.
    public void Reset()
    {
        seen.Clear();
        FinalList = null;
        Accepted = [];
        Searches = Submissions = Rejections = 0;
    }

    [Description("Searches the catalogue by meaning and returns the 5 best matching products with name, description and code. " +
                 "Use it once for each need, with a short description of that need.")]
    public async Task<string> SearchProducts([Description("What the customer needs, in plain words")] string need)
    {
        Searches++;
        var results = await index.SearchAsync(need, 5);
        foreach (var r in results)
        {
            var match = CodePattern.Match(r.Product.Description);
            if (match.Success)
                seen.Add(match.Groups[1].Value);
        }

        Console.WriteLine($"  [tool] SearchProducts(\"{need}\") -> {string.Join(", ", results.Select(r => r.Product.Name))}");
        return string.Join("\n", results.Select(r => $"- {r.Product.Name}: {r.Product.Description}"));
    }

    [Description("Submits the final shopping list. The shop checks every product and its stock, looks up the prices " +
                 "and works out the total. Call it with every chosen product and its quantity. " +
                 "If it reports a problem, change the list and call it again.")]
    public string SubmitShoppingList([Description("The chosen products, each with its code and quantity")] ListItem[] items)
    {
        Submissions++;
        Console.WriteLine($"  [tool] SubmitShoppingList({string.Join(", ", items.Select(i => $"{i.Code} x{i.Quantity}"))})");

        var problems = new List<string>();
        var lines = new List<string>();
        decimal total = 0;

        foreach (var item in items)
        {
            var code = item.Code.Trim().ToUpperInvariant();

            if (!seen.Contains(code))
            {
                problems.Add($"{code} was not in any search result.");
                continue;
            }
            if (item.Quantity < 1 || item.Quantity > 10)
            {
                problems.Add($"{code}: the quantity must be between 1 and 10.");
                continue;
            }

            var product = shop.Get(code);
            if (product is null)
            {
                problems.Add($"{code} does not exist.");
                continue;
            }
            if (product.Stock < item.Quantity)
            {
                problems.Add(product.Stock == 0
                    ? $"{product.Name} ({code}) is out of stock."
                    : $"Only {product.Stock} of {product.Name} ({code}) are in stock.");
                continue;
            }

            // Arithmetic in code, never in the model.
            var lineTotal = product.Price * item.Quantity;
            total += lineTotal;
            lines.Add($"- {product.Name} ({code}): {item.Quantity} x {Euro(product.Price)} = {Euro(lineTotal)}");
        }

        if (problems.Count > 0)
        {
            Rejections++;
            var refusal = "Not accepted. " + string.Join(" ", problems) +
                          " Choose other products from the search results for those needs and submit the whole list again.";
            Console.WriteLine($"         -> {refusal}");
            return refusal;
        }

        Accepted = items.Select(i => i with { Code = i.Code.Trim().ToUpperInvariant() }).ToList();
        FinalList = string.Join("\n", lines) + $"\nTotal: {Euro(total)}";
        Console.WriteLine("         -> accepted");
        return "Accepted. The customer will see this list, written by the shop:\n" + FinalList;
    }

    private static string Euro(decimal price) => "€" + price.ToString("F2", CultureInfo.InvariantCulture);
}