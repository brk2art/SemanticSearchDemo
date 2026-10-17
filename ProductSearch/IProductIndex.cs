public interface IProductIndex
{
    Task AddRangeAsync(IReadOnlyList<Product> products);
    Task<List<(Product Product, float Score)>> SearchAsync(string query, int top = 3);
}