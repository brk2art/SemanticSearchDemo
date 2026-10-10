using System;
using System.Collections.Generic;
using System.Text;

namespace SemanticSearchDemo;

// One test case: a question, and the ids of the products that should be found for it.
public record EvalCase(string Question, int[] ExpectedIds);

public static class RetrievalEval
{
    // The golden set. Ids: 1 Rain jacket, 2 Waterproof boots, 3 Tent,
    // 4 Sleeping bag, 5 Headlamp, 6 Camping stove, 7 Water bottle.
    public static readonly EvalCase[] Cases =
    [
        new("do you sell anything for rain?", [1, 2]),
        new("how much is the tent?", [3]),
        new("is the rain jacket available in red?", [1]),
        new("which is cheaper, the tent or the sleeping bag?", [3, 4]),
        new("what can I use to cook outdoors?", [6]),
        new("how many people fit in the tent?", [3]),
        new("do you have anything to keep drinks cold?", [7]),
        new("I need to see in the dark", [5]),
        new("what should I wear on my feet for a wet hike?", [2]),
        new("something warm to sleep in", [4]),
        new("I want hot food on the trail", [6]),
        new("my hands need to be free while I walk at night", [5]),
    ];

    public static async Task RunAsync(IProductIndex index)
    {
        foreach (var k in new[] { 1, 3 })
        {
            Console.WriteLine($"\nRecall@{k}");
            double total = 0;

            foreach(var c in Cases)
            {
                var results = await index.SearchAsync(c.Question, top: k);
                var foundIds = results.Select(r => r.Product.Id).ToHashSet();

                // Of the products that should be found, what share was in the top k?
                var recall = c.ExpectedIds.Count(foundIds.Contains) / (double)c.ExpectedIds.Length;
                total += recall;

                Console.WriteLine($"  {recall:F2}  {c.Question}");

                if (recall < 1)
                    Console.WriteLine($"        got: {string.Join(", ", results.Select(r => $"{r.Product.Name} ({r.Score:F3})"))}");
            }

            Console.WriteLine($"  ----\n  {total / Cases.Length:F2}  average");
        }
    }
}
