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
            int? categoryId = null, string? sortOrder = null, int limit = CustomerMenuBrowsing.PageSize,
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
        // Separate discovery section; these items also remain in the full catalogue.
        public IReadOnlyList<MenuItemSummary> RecentItems { get; init; }
            = System.Array.Empty<MenuItemSummary>();
        public CustomerMenuLiveState LiveState { get; init; } = CustomerMenuLiveState.Empty;
        // US29: number of dishes with a running promotion (Specials filter).
        public int SpecialsCount { get; init; }
        public int NewCount { get; init; }
        public bool HasMore => Items.Count < TotalCount;
        public int NextLimit => Limit > int.MaxValue - CustomerMenuBrowsing.PageSize
            ? int.MaxValue : Limit + CustomerMenuBrowsing.PageSize;
        public static CustomerMenuPage Empty { get; } = new(
            System.Array.Empty<CustomerMenuCategory>(), System.Array.Empty<MenuItemSummary>(),
            0, 0, CustomerMenuBrowsing.PageSize, "menu");
    }

    // Public browsing metadata only; no order/customer details or unavailable-item names.
    public sealed record CustomerMenuLiveItem(int Id, int CategoryId, string Name,
        decimal Price, string Stamp)
    {
        public bool IsAvailable { get; init; } = true;
    }
    public sealed record CustomerMenuLiveState(string Revision,
        IReadOnlyList<CustomerMenuLiveItem> Items, IReadOnlyList<int> MatchingIds)
    {
        public static CustomerMenuLiveState Empty { get; } = new("",
            System.Array.Empty<CustomerMenuLiveItem>(), System.Array.Empty<int>());
    }

    public static class CustomerMenuBrowsing
    {
        // Twenty fills both four-column and five-column desktop layouts.
        public const int PageSize = 20;
        public static string NormalizeSort(string? value) => value switch
        {
            "name" => "name",
            "name-desc" => "name-desc",
            "price" => "price",
            "price-desc" => "price-desc",
            _ => "menu"
        };
    }
}
