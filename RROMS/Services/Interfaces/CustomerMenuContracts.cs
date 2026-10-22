using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RROMS.Services.Interfaces
{
    public partial interface IOrderService
    {
        Task<CustomerMenuView> GetCustomerMenuAsync(string? search = null,
            int? categoryId = null, CancellationToken cancellationToken = default);
    }

    public sealed record CustomerMenuCategory(int Id, string Name, string Code);
    public sealed record CustomerMenuSection(CustomerMenuCategory Category,
        IReadOnlyList<MenuItemSummary> Items);
    public sealed record CustomerMenuView(IReadOnlyList<CustomerMenuCategory> Categories,
        IReadOnlyList<CustomerMenuSection> Sections)
    {
        public static CustomerMenuView Empty { get; } = new(
            System.Array.Empty<CustomerMenuCategory>(), System.Array.Empty<CustomerMenuSection>());
    }
}
