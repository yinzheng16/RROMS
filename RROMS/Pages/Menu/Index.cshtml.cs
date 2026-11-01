using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;
using System.Security.Claims;
using RROMS.Models;

namespace RROMS.Pages.Menu
{
    // Owner: Member 3. Public browsing. Pages call the service, never the database.
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(131072)]
    public class IndexModel : PageModel
    {
        private readonly IOrderService _menu;
        private readonly ICustomerCartService _carts;
        public IndexModel(IOrderService menu, ICustomerCartService carts) { _menu = menu; _carts = carts; }

        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public int? CategoryId { get; set; }
        [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
        [BindProperty(SupportsGet = true)] public int Limit { get; set; } = CustomerMenuBrowsing.PageSize;
        [BindProperty(SupportsGet = true)] public int? ItemId { get; set; }
        public CustomerMenuPage Menu { get; private set; } = CustomerMenuPage.Empty;
        public MenuItemSummary? SelectedItem { get; private set; }
        public string? DetailNotice { get; private set; }
        public bool ShowingSpecials => CategoryId == RROMS.Services.CustomerMenuBrowseRules.SpecialsCategoryId;
        public bool ShowingNew => CategoryId == RROMS.Services.CustomerMenuBrowseRules.NewCategoryId;
        public string CurrentCategory => ShowingSpecials ? "Specials"
            : ShowingNew ? "New dishes"
            : CategoryId.HasValue
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

        public async Task<IActionResult> OnGetLiveAsync(CancellationToken cancellationToken)
        {
            // Read-only public GET. Like the page itself, this handler calls only the service.
            var page = await _menu.GetCustomerMenuPageAsync(Search, CategoryId, Sort,
                CustomerMenuBrowsing.PageSize, cancellationToken);
            return new JsonResult(page.LiveState, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        public async Task<IActionResult> OnPostAddAsync(int menuItemId, int quantity,
            string? notes, string? returnUrl, CancellationToken cancellationToken)
        {
            bool ajax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            string destination = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Page("/Menu/Index")!;
            if (User.Identity?.IsAuthenticated != true)
            {
                if (ajax) { Response.StatusCode = 401; return new JsonResult(new { message = "Please sign in to add dishes to your cart." }); }
                return RedirectToPage("/Account/Login", new { area = "Identity", returnUrl = destination });
            }
            if (!User.IsInRole(Roles.Customer))
            {
                if (ajax) { Response.StatusCode = 403; return new JsonResult(new { message = "Only customer accounts can add dishes to a cart." }); }
                TempData["ErrorMessage"] = "Only customer accounts can place orders.";
                return LocalRedirect(destination);
            }
            var result = ModelState.IsValid
                ? await _carts.AddAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, menuItemId, quantity, notes, cancellationToken)
                : new CartOperation(CartResult.Invalid, "Check your quantity and dish note.");
            if (ajax)
            {
                Response.StatusCode = result.Succeeded ? 200 : result.Status == CartResult.Forbidden ? 403 : 422;
                return new JsonResult(new { message = result.Message, count = await _carts.CountAsync(
                    User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken), success = result.Succeeded });
            }
            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return LocalRedirect(destination);
        }

        public static (string Primary, string Secondary) CategoryLabel(string name)
        {
            var parts = name.Split('·', 2, StringSplitOptions.TrimEntries);
            return (parts[0], parts.Length > 1 ? parts[1] : "");
        }
    }
}
