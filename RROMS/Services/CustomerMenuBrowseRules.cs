using System;
using System.Collections.Generic;
using System.Linq;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 3. Pure rules; source order comes from current category DisplayOrder.
    public static class CustomerMenuBrowseRules
    {
        public static CustomerMenuPage Build(IReadOnlyList<MenuItemSummary> source,
            string? search, int? categoryId, string? sortOrder, int limit)
        {
            var all = source.Where(item => item.IsAvailable && item.Price > 0).ToArray();
            var categories = all.GroupBy(item => item.CategoryId)
                .Select(group => new CustomerMenuCategory(group.Key,
                    group.First().CategoryName, group.First().CategoryCode) { ItemCount = group.Count() }).ToArray();
            string term = (search ?? "").Trim();
            if (term.Length > 100) term = term[..100];
            var matching = all.Where(item => (!categoryId.HasValue || item.CategoryId == categoryId.Value)
                && (term.Length == 0 || item.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || item.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || item.CategoryName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || item.CategoryCode.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
            string sort = CustomerMenuBrowsing.NormalizeSort(sortOrder);
            IEnumerable<MenuItemSummary> ordered = sort switch
            {
                "name" => matching.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id),
                "name-desc" => matching.OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id),
                "price" => matching.OrderBy(item => item.Price).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id),
                "price-desc" => matching.OrderByDescending(item => item.Price).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id),
                _ => matching
            };
            limit = Math.Clamp(limit, CustomerMenuBrowsing.PageSize,
                Math.Max(CustomerMenuBrowsing.PageSize, matching.Length));
            return new CustomerMenuPage(categories, ordered.Take(limit).ToArray(),
                matching.Length, all.Length, limit, sort);
        }
    }
}
