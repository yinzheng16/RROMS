using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    public partial class OrderService
    {
        public async Task<CustomerMenuPage> GetCustomerMenuPageAsync(string? search = null,
            int? categoryId = null, string? sortOrder = null, int limit = 12,
            CancellationToken cancellationToken = default)
        {
            var all = await GetOrderableMenuItemsAsync(cancellationToken);
            return CustomerMenuBrowseRules.Build(all, search, categoryId, sortOrder, limit);
        }

        public async Task<CustomerMenuView> GetCustomerMenuAsync(string? search = null,
            int? categoryId = null, CancellationToken cancellationToken = default)
        {
            // This accessor queries current DisplayOrder on every request and
            // excludes unavailable dishes. GroupBy preserves that ordered input.
            var all = await GetOrderableMenuItemsAsync(cancellationToken);
            var categories = all.GroupBy(item => item.CategoryId)
                .Select(group => new CustomerMenuCategory(group.Key,
                    group.First().CategoryName, group.First().CategoryCode)).ToArray();
            string term = search?.Trim() ?? "";
            var visible = all.Where(item => (!categoryId.HasValue || item.CategoryId == categoryId)
                && (term.Length == 0 || item.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || item.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || item.CategoryName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || item.CategoryCode.Contains(term, StringComparison.OrdinalIgnoreCase)));
            var sections = visible.GroupBy(item => item.CategoryId)
                .Select(group => new CustomerMenuSection(
                    categories.Single(category => category.Id == group.Key), group.ToArray())).ToArray();
            return new CustomerMenuView(categories, sections);
        }
    }
}
