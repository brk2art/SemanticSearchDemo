using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// With stdio, stdout carries the protocol messages. Anything else written there breaks it.
// ShopTools prints [tool] lines with Console.WriteLine, so all console output goes to stderr.
Console.SetOut(Console.Error);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<ShopTools>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ShopMcpTools>();

await builder.Build().RunAsync();

// The MCP layer is a thin adapter, like a controller over a service:
// ShopTools knows nothing about MCP, and this class knows nothing about inventory.
[McpServerToolType]
public class ShopMcpTools(ShopTools shop)
{
    [McpServerTool(Name = "GetPriceAndStock"), Description("Gets the current price in euros and the number in stock for one product, by its product code.")]
    public string GetPriceAndStock([Description("The product code, for example TN-200")] string code)
        => shop.GetPriceAndStock(code);

    [McpServerTool(Name = "FindProducts"), Description("Lists every product whose name contains a word, with code, price and stock, most expensive first. " +
                                "Use it for questions about all products of a kind, such as the cheapest or most expensive tent, " +
                                "or to check whether the shop sells something.")]
    public string FindProducts([Description("One word from the product name, for example tent")] string word)
        => shop.FindProducts(word);
}
