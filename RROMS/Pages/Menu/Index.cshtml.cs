using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Menu
{
    // Owner: Member 3. Public browsing. Pages call the service, never the database.
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class IndexModel : PageModel
    {
        private readonly IOrderService _menu;
        public IndexModel(IOrderService menu) { _menu = menu; }

        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public int? CategoryId { get; set; }
        [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
        [BindProperty(SupportsGet = true)] public int Limit { get; set; } = CustomerMenuBrowsing.PageSize;
        [BindProperty(SupportsGet = true)] public int? ItemId { get; set; }
        public CustomerMenuPage Menu { get; private set; } = CustomerMenuPage.Empty;
        public MenuItemSummary? SelectedItem { get; private set; }
        public string? DetailNotice { get; private set; }
        public string CurrentCategory => CategoryId.HasValue
            ? Menu.Categories.FirstOrDefault(c => c.Id == CategoryId)?.Name ?? "Selected category"
            : "All dishes";
        public bool HasFilters => Search is not null || CategoryId.HasValue || Sort != "menu";

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
            if (Search?.Length > 100) Search = Search[..100];
            Sort = CustomerMenuBrowsing.NormalizeSort(Sort);
            Menu = await _menu.GetCustomerMenuPageAsync(Search, CategoryId, Sort, Limit, cancellationToken);
            Limit = Menu.Limit;
            if (ItemId.HasValue)
            {
                SelectedItem = await _menu.GetOrderableMenuItemAsync(ItemId.Value, cancellationToken);
                if (SelectedItem is null) DetailNotice = "That dish is no longer available. Explore another favourite below.";
            }
        }

        public static (string Primary, string Secondary) CategoryLabel(string name)
        {
            var parts = name.Split('·', 2, StringSplitOptions.TrimEntries);
            return (parts[0], parts.Length > 1 ? parts[1] : "");
        }
    }
}
