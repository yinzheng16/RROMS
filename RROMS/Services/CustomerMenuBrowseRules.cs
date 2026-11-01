using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 3. Pure rules; source order comes from current category DisplayOrder.
    public static class CustomerMenuBrowseRules
    {
        // US29: CategoryId=-1 shows only dishes with a running promotion ("Specials").
        public const int SpecialsCategoryId = -1;
        // CategoryId=-2 shows only dishes added in the last 7 days ("New"), same rule as SelectRecent.
        public const int NewCategoryId = -2;
        public const int NewDishDays = 7;

        private static bool IsNew(MenuItemSummary item, DateTime now)
            => item.CreatedAt >= now.AddDays(-NewDishDays) && item.CreatedAt <= now;

        public static IReadOnlyList<MenuItemSummary> SelectRecent(
            IReadOnlyList<MenuItemSummary> source, DateTime now,
            IEnumerable<string> demoNames)
        {
            // Existing CreatedAt values use server-local DateTime.Now.
            // Keep demoNames in the signature so existing service callers still work.
            // A dish's name or import source does not determine whether it is new.
            DateTime cutoff = now.AddDays(-NewDishDays);
            return source.Where(item => item.Price >= 0
                    && item.CreatedAt >= cutoff && item.CreatedAt <= now)
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .ToArray();
        }

        private static (string Prefix, string Number) CodeOrderParts(string? code)
        {
            string value = (code ?? "").Trim();
            int start = value.Length;
            while (start > 0 && value[start - 1] >= '0' && value[start - 1] <= '9') start--;
            string number = value[start..].TrimStart('0');
            return (value[..start], number.Length == 0 ? "0" : number);
        }

        private static IEnumerable<MenuItemSummary> InMenuOrder(IEnumerable<MenuItemSummary> items)
        {
            // The service supplies category DisplayOrder. GroupBy keeps its first-seen
            // category sequence; only the items within each category are rearranged.
            return items.GroupBy(item => item.CategoryId).SelectMany(group => group
                .Select(item => new { Item = item, Code = CodeOrderParts(item.Code) })
                .OrderBy(row => row.Code.Prefix, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Code.Number.Length)
                .ThenBy(row => row.Code.Number, StringComparer.Ordinal)
                .ThenBy(row => row.Item.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Item.Id)
                .Select(row => row.Item));
        }

        private static string Stamp(object value)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

        private static CustomerMenuLiveState CreateLiveState(
            IReadOnlyList<MenuItemSummary> all, IReadOnlyList<MenuItemSummary> matching)
        {
            var entries = all.Select(item => new CustomerMenuLiveItem(item.Id, item.CategoryId,
                item.Name, item.Price, Stamp(new
                {
                    item.Name,
                    item.Code,
                    item.Description,
                    item.Price,
                    item.OriginalPrice,
                    item.IsAvailable,
                    item.ImageUrl,
                    item.CategoryId,
                    item.CategoryName,
                    item.CategoryCode
                }))
            { IsAvailable = item.IsAvailable }).ToArray();
            // Input sequence follows current category DisplayOrder, so reordering is detected.
            // HasOrders is deliberately absent: placing an order must not trigger a menu notice.
            return new CustomerMenuLiveState(Stamp(entries), entries,
                matching.Select(item => item.Id).ToArray());
        }

        public static CustomerMenuPage Build(IReadOnlyList<MenuItemSummary> source,
            string? search, int? categoryId, string? sortOrder, int limit, DateTime? now = null)
        {
            // Existing CreatedAt values use server-local DateTime.Now (see SelectRecent).
            DateTime clock = now ?? DateTime.Now;
            // Base-price validation happens in the service. A 100% promotion may be free.
            var all = InMenuOrder(source.Where(item => item.Price >= 0)).ToArray();
            var categories = all.GroupBy(item => item.CategoryId)
                .Select(group => new CustomerMenuCategory(group.Key,
                    group.First().CategoryName, group.First().CategoryCode)
                { ItemCount = group.Count() }).ToArray();
            string term = (search ?? "").Trim();
            if (term.Length > 100) term = term[..100];
            bool specials = categoryId == SpecialsCategoryId;
            bool newDishes = categoryId == NewCategoryId;
            var matching = all.Where(item => (!categoryId.HasValue
                    || (specials ? item.OriginalPrice.HasValue
                        : newDishes ? IsNew(item, clock)
                        : item.CategoryId == categoryId.Value))
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
            // Twenty-item batches fill both four-column and five-column desktop rows.
            // Use long arithmetic so even an int.MaxValue query cannot overflow.
            long requested = Math.Max(limit, CustomerMenuBrowsing.PageSize);
            long rounded = ((requested + CustomerMenuBrowsing.PageSize - 1)
                / CustomerMenuBrowsing.PageSize) * CustomerMenuBrowsing.PageSize;
            limit = (int)Math.Min(rounded,
                Math.Max(CustomerMenuBrowsing.PageSize, matching.Length));
            return new CustomerMenuPage(categories, ordered.Take(limit).ToArray(),
                matching.Length, all.Length, limit, sort)
            {
                LiveState = CreateLiveState(all, matching),
                SpecialsCount = all.Count(item => item.OriginalPrice.HasValue),
                NewCount = all.Count(item => IsNew(item, clock))
            };
        }
    }
}
