// A question and the request the model should extract. Category "" means no category expected.
public record ExtractCase(string Question, string Kind, string[] Codes, string Category = "", int Quantity = 1);

public static class ExtractEval
{
    public static readonly ExtractCase[] Cases =
    [
        new("how much is the family tent?", "price", ["TN-400"]),
        new("is the sleeping bag in stock?", "stock", ["SB-300"]),
        new("how much does the headlamp cost?", "price", ["HL-050"]),
        new("do you have the ultralight tent in stock?", "stock", ["TN-100"]),
        new("what does a gas canister cost?", "price", ["GC-420"]),
        new("how many water filters do you have?", "stock", ["WF-070"]),
        new("which is cheaper, the lantern or the flashlight?", "compare", ["LN-060", "FL-055"]),
        new("what do the stove and a gas canister cost together?", "price", ["CS-400", "GC-420"]),
        new("is the summer sleeping bag cheaper than the sleeping bag?", "compare", ["SB-310", "SB-300"]),
        new("what's the lantern going for?", "price", ["LN-060"]),
        new("can I buy a summer sleeping bag today?", "stock", ["SB-310"]),
        new("is the insect repellent under 10 euros?", "price", ["IR-710"]),
        new("how much would two gas canisters be?", "price", ["GC-420"], Quantity: 2),
        new("which tent is the most expensive?", "most_expensive", [], Category: "tent"),
        new("how much is a kayak?", "price", []),
    ];

    public static async Task RunAsync(IProductIndex index, RequestExtractor extractor)
    {
        int parsed = 0, kindOk = 0, codesOk = 0, categoryOk = 0, quantityOk = 0, allOk = 0;

        Console.WriteLine($"\nExtraction ({Cases.Length} questions)");
        foreach (var c in Cases)
        {
            var results = await index.SearchAsync(c.Question, 3);
            var productList = string.Join("\n", results.Select(r => $"- {r.Product.Name}: {r.Product.Description}"));

            var (request, json) = await extractor.ExtractAsync(c.Question, productList);

            if (request is null)
            {
                Console.WriteLine($"\n  NO JSON  {c.Question}\n           raw: {json}");
                continue;
            }
            parsed++;

            var codes = (request.Codes ?? []).Select(x => x.Trim().ToUpperInvariant()).ToHashSet();
            var k = string.Equals(request.Kind?.Trim(), c.Kind, StringComparison.OrdinalIgnoreCase);
            // Category questions use FindProducts, so their codes don't matter.
            var cd = c.Category != "" || codes.SetEquals(c.Codes);
            // Strict: an empty expected category means the answer must be empty too.
            var cat = string.Equals((request.Category ?? "").Trim(), c.Category, StringComparison.OrdinalIgnoreCase);
            var q = request.Quantity == c.Quantity;

            if (k) kindOk++;
            if (cd) codesOk++;
            if (cat) categoryOk++;
            if (q) quantityOk++;
            if (k && cd && cat && q) allOk++;

            var wrong = string.Join(", ", new[] { k ? null : "kind", cd ? null : "codes", cat ? null : "category", q ? null : "quantity" }
                .Where(w => w is not null));

            Console.WriteLine($"\n  {(wrong == "" ? "ok" : "WRONG"),-8} {c.Question}");
            Console.WriteLine($"           json:     {json}");
            
            if (wrong != "")
            {
                Console.WriteLine($"           expected: kind={c.Kind}, codes=[{string.Join(", ", c.Codes)}], category={c.Category}, quantity={c.Quantity}  (wrong: {wrong})");
                Console.WriteLine($"           products: {string.Join(", ", results.Select(r => r.Product.Name))}");
            }
        }

        Console.WriteLine("\n  ----");
        Console.WriteLine($"  Valid JSON:      {parsed} of {Cases.Length}");
        Console.WriteLine($"  Kind right:      {kindOk}");
        Console.WriteLine($"  Codes right:     {codesOk}");
        Console.WriteLine($"  Category right:  {categoryOk}");
        Console.WriteLine($"  Quantity right:  {quantityOk}");
        Console.WriteLine($"  All right:       {allOk} of {Cases.Length}");
    }
}