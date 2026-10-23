using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RROMS.Services.Interfaces
{
    public partial interface IOrderService
    {
        Task<CustomerMenuView> GetCustomerMenuAsync(string? search = null,
            int? categoryId = null, CancellationToken cancellationToken = default);

        Task<CustomerMenuPage> GetCustomerMenuPageAsync(string? search = null,
            int? categoryId = null, string? sortOrder = null, int limit = 12,
            CancellationToken cancellationToken = default);
    }

    public sealed record CustomerMenuCategory(int Id, string Name, string Code)
    {
        public int ItemCount { get; init; }
    }
    public sealed record CustomerMenuSection(CustomerMenuCategory Category,
        IReadOnlyList<MenuItemSummary> Items);
    public sealed record CustomerMenuView(IReadOnlyList<CustomerMenuCategory> Categories,
        IReadOnlyList<CustomerMenuSection> Sections)
    {
        public static CustomerMenuView Empty { get; } = new(
            System.Array.Empty<CustomerMenuCategory>(), System.Array.Empty<CustomerMenuSection>());
    }
    public sealed record CustomerMenuPage(IReadOnlyList<CustomerMenuCategory> Categories,
        IReadOnlyList<MenuItemSummary> Items, int TotalCount, int CatalogueCount,
        int Limit, string SortOrder)
    {
        public bool HasMore => Items.Count < TotalCount;
        public int NextLimit => Limit > int.MaxValue - CustomerMenuBrowsing.PageSize
            ? int.MaxValue : Limit + CustomerMenuBrowsing.PageSize;
        public static CustomerMenuPage Empty { get; } = new(
            System.Array.Empty<CustomerMenuCategory>(), System.Array.Empty<MenuItemSummary>(),
            0, 0, CustomerMenuBrowsing.PageSize, "menu");
    }

    public static class CustomerMenuBrowsing
    {
        public const int PageSize = 12;
        public static string NormalizeSort(string? value) => value switch
        {
            "name" => "name", "name-desc" => "name-desc",
            "price" => "price", "price-desc" => "price-desc", _ => "menu"
        };
    }
}
