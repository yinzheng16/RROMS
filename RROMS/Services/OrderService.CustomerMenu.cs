using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;
using RROMS.Services.Interfaces;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Services
{
    public partial class OrderService
    {
        public async Task<CustomerMenuPage> GetCustomerMenuPageAsync(string? search = null,
            int? categoryId = null, string? sortOrder = null, int limit = CustomerMenuBrowsing.PageSize,
            CancellationToken cancellationToken = default, string? customerId = null)
        {
            var all = await GetBrowsableCustomerMenuItemsAsync(cancellationToken);
            var page = CustomerMenuBrowseRules.Build(all, search, categoryId, sortOrder, limit);
            if (!string.IsNullOrWhiteSpace(search) || categoryId.HasValue || page.SortOrder != "menu")
                return page;
            var (popular, forYou) = await GetMenuPicksAsync(all, customerId, cancellationToken);
            return page with
            {
                RecentItems = CustomerMenuBrowseRules.SelectRecent(all, DateTime.Now,
                    MenuDemoCatalogue.Items.Select(entry => entry.Name)),
                PopularItems = popular,
                ForYouItems = forYou
            };
        }

        // US30: recommendations from saved orders (no new tables). Cancelled orders and
        // removed portions are ignored. Only dishes that can be ordered right now are shown.
        private const int PickLimit = 10;

        private async Task<(IReadOnlyList<MenuItemSummary> Popular, IReadOnlyList<MenuItemSummary> ForYou)>
            GetMenuPicksAsync(IReadOnlyList<MenuItemSummary> all, string? customerId, CancellationToken ct)
        {
            var available = all.Where(item => item.IsAvailable).ToDictionary(item => item.Id);
            var counted = _context.OrderItems.AsNoTracking()
                .Where(line => line.Quantity > 0 && line.Order.Status != OrderStatus.Cancelled);

            // Popular this week: most portions ordered in the last 7 days.
            DateTime weekAgo = DateTime.Now.AddDays(-7);
            var weekly = await counted.Where(line => line.Order.CreatedAt >= weekAgo)
                .GroupBy(line => line.MenuItemId)
                .Select(group => new { Id = group.Key, Portions = group.Sum(line => line.Quantity) })
                .OrderByDescending(row => row.Portions).ThenBy(row => row.Id)
                .Take(PickLimit * 3).ToListAsync(ct);
            var popular = weekly.Where(row => available.ContainsKey(row.Id))
                .Select(row => available[row.Id]).Take(PickLimit).ToArray();

            if (string.IsNullOrEmpty(customerId))
                return (popular, Array.Empty<MenuItemSummary>());

            // Recommended for you: the customer's top 3 categories by portions ordered,
            // then the dishes in those categories, most popular overall first.
            var favourites = await counted.Where(line => line.Order.UserId == customerId)
                .GroupBy(line => line.MenuItem.CategoryId)
                .Select(group => new { CategoryId = group.Key, Portions = group.Sum(line => line.Quantity) })
                .OrderByDescending(row => row.Portions).ThenBy(row => row.CategoryId)
                .Take(3).ToListAsync(ct);
            if (favourites.Count == 0)
                return (popular, Array.Empty<MenuItemSummary>());

            int[] categoryIds = favourites.Select(row => row.CategoryId).ToArray();
            var allTime = await counted.Where(line => categoryIds.Contains(line.MenuItem.CategoryId))
                .GroupBy(line => line.MenuItemId)
                .Select(group => new { Id = group.Key, Portions = group.Sum(line => line.Quantity) })
                .ToDictionaryAsync(row => row.Id, row => row.Portions, ct);
            var rank = categoryIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
            var forYou = available.Values.Where(item => rank.ContainsKey(item.CategoryId))
                .OrderBy(item => rank[item.CategoryId])
                .ThenByDescending(item => allTime.GetValueOrDefault(item.Id))
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Take(PickLimit).ToArray();
            return (popular, forYou);
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
