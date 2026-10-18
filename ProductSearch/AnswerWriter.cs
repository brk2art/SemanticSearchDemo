using System.Globalization;

// Turns an extracted ShopRequest into an answer. Every fact in the sentence comes from data.
public static class AnswerWriter
{
    // allowedCodes = the codes in the search results. The model's codes are checked against
    // them like any other input: a code it was never shown is ignored.
    public static string Write(ShopRequest r, ShopTools tools, ISet<string> allowedCodes)
    {
        var kind = (r.Kind ?? "").Trim().ToLowerInvariant();

        if (kind is "most_expensive" or "cheapest")
        {
            var category = (r.Category ?? "").Trim().ToLowerInvariant();
            if (category == "")
                return ShopAssistant.Refusal;

            var all = tools.FindAll(category);
            if (all.Count == 0)
                return $"We don't sell any {category} products.";

            var pick = kind == "most_expensive" ? all.MaxBy(i => i.Price)! : all.MinBy(i => i.Price)!;
            var word = kind == "most_expensive" ? "most expensive" : "cheapest";
            return $"The {word} {category} is the {Lower(pick.Name)} at {Euro(pick.Price)}.";
        }

        var items = (r.Codes ?? [])
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct()
            .Where(allowedCodes.Contains)
            .Select(tools.Get)
            .OfType<StockItem>()
            .ToList();

        if (items.Count == 0)
            return ShopAssistant.Refusal;

        var quantity = Math.Max(1, r.Quantity);

        switch (kind)
        {
            case "stock":
                return string.Join(" ", items.Select(i => i.Stock == 0
                    ? $"The {Lower(i.Name)} is out of stock."
                    : $"We have {i.Stock} of the {Lower(i.Name)} in stock."));

            case "compare" when items.Count >= 2:
                var cheapest = items.MinBy(i => i.Price)!;
                var others = items.Where(i => i != cheapest).Select(i => $"the {Lower(i.Name)} ({Euro(i.Price)})");
                return $"The {Lower(cheapest.Name)} is cheaper at {Euro(cheapest.Price)}, against {string.Join(" and ", others)}.";

            case "price":
            case "compare":
                var sentences = items.Select(i => $"The {Lower(i.Name)} costs {Euro(i.Price)}.").ToList();
                if (items.Count > 1 || quantity > 1)
                {
                    // Arithmetic in code, never in the model.
                    var total = items.Sum(i => i.Price) * quantity;
                    sentences.Add(items.Count == 1
                        ? $"{quantity} of them cost {Euro(total)}."
                        : $"Together they cost {Euro(total)}.");
                }
                return string.Join(" ", sentences);

            default:
                return ShopAssistant.Refusal;
        }
    }

    private static string Euro(decimal price) => "€" + price.ToString("F2", CultureInfo.InvariantCulture);

    private static string Lower(string name) => name.ToLowerInvariant();
}