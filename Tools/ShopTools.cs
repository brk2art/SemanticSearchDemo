using System.ComponentModel;

// Live data the model can ask for. In a real shop this would query the database or ERP.
public class ShopTools
{
    // Every code the model asked for, so the evaluation can check the calls.
    public List<string> Calls { get; } = [];

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