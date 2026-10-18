using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

// One product's live data, as a value code can use (not text for a model to read).
public record StockItem(string Code, string Name, decimal Price, int Stock);

// Live data the model can ask for. In a real shop this would query the database or ERP.
public class ShopTools
{
    // Every code the model asked for, so the evaluation can check the calls.
    public List<string> Calls { get; } = [];

    // Everything the tools returned, so the evaluation can check the answer's numbers against it.
    public List<string> Results { get; } = [];

    // Our Product has no Code field: the code is written inside the description ("Code TN-200.").
    // In a real system code and category would be columns. Here we read the code back with a regex.
    private static readonly Regex CodePattern = new(@"Code ([A-Z]{2}-\d{3})");

    // Product names by code, read from the catalogue once.
    private static readonly Dictionary<string, string> Names = Catalog.Products
        .Where(p => CodePattern.IsMatch(p.Description))
        .ToDictionary(p => CodePattern.Match(p.Description).Groups[1].Value, p => p.Name);

    // For code, not for the model: the same data as the tools, returned as values.
    // They record calls too, so ToolEval can check them the same way.
    public StockItem? Get(string code)
    {
        var c = code.Trim().ToUpperInvariant();
        Calls.Add(c);

        var item = Inventory.TryGetValue(c, out var data) && Names.TryGetValue(c, out var name)
            ? new StockItem(c, name, data.Price, data.Stock)
            : null;

        // Recorded so the evaluation can check the answer's prices against real data.
        if (item is not null)
            Results.Add($"{c}: price €{item.Price.ToString("F2", CultureInfo.InvariantCulture)}");

        return item;
    }

    public List<StockItem> FindAll(string word)
    {
        var w = word.Trim().ToLowerInvariant();
        Calls.Add($"find:{w}");

        bool Matches(string name) =>
            name.Contains(w, StringComparison.OrdinalIgnoreCase) ||
            (w.EndsWith('s') && name.Contains(w[..^1], StringComparison.OrdinalIgnoreCase));

        var items = Names
            .Where(n => Matches(n.Value) && Inventory.ContainsKey(n.Key))
            .Select(n => new StockItem(n.Key, n.Value, Inventory[n.Key].Price, Inventory[n.Key].Stock))
            .ToList();

        Results.AddRange(items.Select(i => $"{i.Code}: price €{i.Price.ToString("F2", CultureInfo.InvariantCulture)}"));
        return items;
    }

    [Description("Lists every product whose name contains a word, with code, price and stock, most expensive first. " +
                 "Use it for questions about all products of a kind, such as the cheapest or most expensive tent, " +
                 "or to check whether the shop sells something.")]
    public string FindProducts([Description("One word from the product name, for example tent")] string word)
    {
        var w = word.Trim().ToLowerInvariant();
        Calls.Add($"find:{w}");

        // "tents" should still find "Family tent".
        bool Matches(string name) =>
            name.Contains(w, StringComparison.OrdinalIgnoreCase) ||
            (w.EndsWith('s') && name.Contains(w[..^1], StringComparison.OrdinalIgnoreCase));

        var lines = Catalog.Products
            .Where(p => Matches(p.Name))
            .Select(p => (p.Name, Code: CodePattern.Match(p.Description).Groups[1].Value))
            .Where(m => Inventory.ContainsKey(m.Code))
            .Select(m => (m.Name, m.Code, Item: Inventory[m.Code]))
            .OrderByDescending(m => m.Item.Price)
            .Select(m => $"{m.Name} ({m.Code}): price €{m.Item.Price:F2}, " +
                         (m.Item.Stock == 0 ? "out of stock" : $"{m.Item.Stock} in stock"))
            .ToList();

        var result = lines.Count == 0
            ? $"The shop sells no products with \"{w}\" in the name."
            : string.Join("\n", lines);

        Results.Add(result);
        Console.WriteLine($"  [tool] FindProducts(\"{w}\") -> {result.Replace("\n", " | ")}");
        return result;
    }

    private static readonly Dictionary<string, (decimal Price, int Stock)> Inventory =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["RJ-100"] = (89.95m, 12),
            ["WB-210"] = (129.00m, 7),
            ["TN-200"] = (179.00m, 4),
            ["SB-300"] = (99.00m, 0),
            ["HL-050"] = (34.95m, 25),
            ["CS-400"] = (49.95m, 9),
            ["WT-075"] = (24.95m, 40),
            ["TN-400"] = (349.00m, 2),
            ["TN-100"] = (299.00m, 0),
            ["DJ-120"] = (159.00m, 6),
            ["RP-110"] = (29.95m, 18),
            ["TR-220"] = (119.00m, 10),
            ["HS-230"] = (64.95m, 0),
            ["SB-310"] = (59.95m, 5),
            ["SM-320"] = (69.95m, 8),
            ["LN-060"] = (39.95m, 14),
            ["FL-055"] = (27.95m, 20),
            ["CP-410"] = (44.95m, 11),
            ["GC-420"] = (6.95m, 120),
            ["TF-080"] = (32.95m, 9),
            ["CB-090"] = (89.00m, 3),
            ["WF-070"] = (79.95m, 6),
            ["BP-500"] = (139.00m, 5),
            ["BP-510"] = (59.00m, 13),
            ["TP-600"] = (74.95m, 7),
            ["FA-700"] = (19.95m, 30),
            ["IR-710"] = (8.95m, 45),
            ["SS-720"] = (12.95m, 0),
            ["CM-800"] = (22.95m, 15),
            ["MT-810"] = (49.00m, 8),
        };

    // The Description texts are sent to the model. They are how it decides when to call this.
    [Description("Gets the current price in euros and the number in stock for one product, by its product code.")]
    public string GetPriceAndStock([Description("The product code, for example TN-200")] string code)
    {
        Calls.Add(code.Trim().ToUpperInvariant());

        var result = Lookup(code);
        Results.Add(result);

        // Printed so you can see what the model asked for and what it got back.
        Console.WriteLine($"  [tool] GetPriceAndStock(\"{code}\") -> {result}");

        return result;
    }

    // The actual lookup. It has no [Description], and AIFunctionFactory only wraps
    // GetPriceAndStock, so the model never sees this method.
    private string Lookup(string code)
    {
        if (!Inventory.TryGetValue(code.Trim(), out var item))
            return $"No product with code {code}.";

        var stock = item.Stock == 0 ? "out of stock" : $"{item.Stock} in stock";
        return $"{code.Trim().ToUpperInvariant()}: price €{item.Price:F2}, {stock}.";
    }
}