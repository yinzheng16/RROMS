using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Menu
{
    // Owner: Member 3. Public browsing; all menu access goes through the service.
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class IndexModel : PageModel
    {
        private readonly IOrderService _menu;
        public IndexModel(IOrderService menu) { _menu = menu; }

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }
        [BindProperty(SupportsGet = true)]
        public int? CategoryId { get; set; }
        public CustomerMenuView Menu { get; private set; } = CustomerMenuView.Empty;

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
            if (Search?.Length > 100) Search = Search[..100];
            Menu = await _menu.GetCustomerMenuAsync(Search, CategoryId, cancellationToken);
        }
    }
}
