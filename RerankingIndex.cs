using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

// Two-stage retrieval: the inner index finds candidates quickly,
// then a chat model reads them next to the question and reorders them.
public class RerankingIndex(IProductIndex inner, IChatClient chat, int candidates = 10) : IProductIndex
{
    // One model call per distinct question, however many results are asked for.
    private readonly Dictionary<string, List<(Product Product, float Score)>> _cache = [];

    public Task AddRangeAsync(IReadOnlyList<Product> products) => inner.AddRangeAsync(products);

    public async Task<List<(Product Product, float Score)>> SearchAsync(string query, int top = 3)
    {
        if (!_cache.TryGetValue(query, out var ranked))
        {
            ranked = await RerankAsync(query);
            _cache[query] = ranked;
        }

        return ranked.Take(top).ToList();
    }

    private async Task<List<(Product Product, float Score)>> RerankAsync(string query)
    {
        // Stage 1: cheap and broad.
        var found = await inner.SearchAsync(query, candidates);

        // Stage 2: slow and careful, on a short list only.
        var list = string.Join("\n", found.Select((r, i) => $"{i + 1}. {r.Product.Name}: {r.Product.Description}"));

        var prompt = $"""
            You rank products for a shop search.

            Customer request: {query}

            Candidates:
            {list}

            Order the candidates by how well they meet the customer's need, best first.
            Think about what the customer wants to do, not which words match.
            Reply with the candidate numbers only, separated by commas. Example: 4, 1, 7
            """;

        var response = await chat.GetResponseAsync(prompt, new ChatOptions { Temperature = 0 });

        // Read the numbers the model returned, ignoring anything out of range.
        var order = Regex.Matches(response.Text, @"\d+")
            .Select(m => int.Parse(m.Value) - 1)
            .Where(i => i >= 0 && i < found.Count)
            .Distinct()
            .ToList();

        // Candidates the model left out keep their original order, after the chosen ones.
        order.AddRange(Enumerable.Range(0, found.Count).Except(order));

        return order.Select(i => found[i]).ToList();
    }
}