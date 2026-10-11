using System.Text.Json;
using Microsoft.Extensions.AI;

// Write-path step: adds, to each product, the situations a customer would want it in.
// The result is used for search only; it is never shown to the customer.
public static class Enricher
{
    private const string CacheFile = "uses.json";

    public static async Task<Product[]> AddUsesAsync(Product[] products, IChatClient chat)
    {
        // One model call per product is slow, so results are kept on disk.
        var cache = File.Exists(CacheFile)
            ? JsonSerializer.Deserialize<Dictionary<int, string>>(await File.ReadAllTextAsync(CacheFile))!
            : [];

        var enriched = new List<Product>();
        foreach (var p in products)
        {
            if (!cache.TryGetValue(p.Id, out var uses))
            {
                var prompt = $"""
                    Product: {p.Name}. {p.Description}

                    Write five short needs a customer might express that this specific product meets.
                    Use only what the description says or directly implies.
                    Focus on what sets it apart from similar products.
                    Output exactly five lines and nothing else: no introduction, no numbering, no bullets.
                    At most eight words per line.
                    """;

                var response = await chat.GetResponseAsync(prompt, new ChatOptions { Temperature = 0 });
                uses = string.Join("; ", response.Text
                   .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Where(line => !line.EndsWith(':'))
                   .Select(line => line.TrimStart('*', '-', ' ')));

                cache[p.Id] = uses;
                Console.WriteLine($"  {p.Name}: {uses}");
            }

            enriched.Add(p with { Uses = uses });
        }

        await File.WriteAllTextAsync(CacheFile,
            JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));

        return enriched.ToArray();
    }
}