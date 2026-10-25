using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;
using RROMS.Services.Interfaces;
using RROMS.Data;

namespace RROMS.Services
{
    public partial class OrderService
    {
        public async Task<CustomerMenuPage> GetCustomerMenuPageAsync(string? search = null,
            int? categoryId = null, string? sortOrder = null, int limit = CustomerMenuBrowsing.PageSize,
            CancellationToken cancellationToken = default)
        {
            var all = await GetBrowsableCustomerMenuItemsAsync(cancellationToken);
            var page = CustomerMenuBrowseRules.Build(all, search, categoryId, sortOrder, limit);
            if (!string.IsNullOrWhiteSpace(search) || categoryId.HasValue || page.SortOrder != "menu")
                return page;
            return page with
            {
                RecentItems = CustomerMenuBrowseRules.SelectRecent(all, DateTime.Now,
                    MenuDemoCatalogue.Items.Select(entry => entry.Name))
            };
        }

        // Browsing includes unavailable dishes. Cart/order accessors remain availability-filtered.
        private async Task<IReadOnlyList<MenuItemSummary>> GetBrowsableCustomerMenuItemsAsync(
            CancellationToken cancellationToken)
            => await WithCustomerPricesAsync(await ItemSummaries(_context.MenuItems.AsNoTracking()
                .Where(item => item.Price > 0)
                .OrderBy(item => item.Category.DisplayOrder).ThenBy(item => item.Category.Name)
                .ThenBy(item => item.CategoryId).ThenBy(item => item.Name).ThenBy(item => item.Id))
                .ToListAsync(cancellationToken), cancellationToken);

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
