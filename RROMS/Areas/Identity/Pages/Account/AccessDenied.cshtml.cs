using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Areas.Identity.Pages.Account
{
    // US06 (Owner: Member 1): shown when a logged-in user opens a page their role can't use,
    // e.g. Staff opening /Admin/Menu. Replaces the built-in Identity AccessDenied page.
    public class AccessDeniedModel : PageModel
    {
        private readonly IAccountService _accounts;

        public AccessDeniedModel(IAccountService accounts)
        {
            _accounts = accounts;
        }

        public string StartPage { get; private set; } = "/";
        public string RoleName { get; private set; } = "Guest";
        public string AreaName { get; private set; } = "Restricted";
        public string? Email { get; private set; }

        // Identity sends us here as /Identity/Account/AccessDenied?ReturnUrl=/Admin/Menu
        public void OnGet(string? returnUrl = null)
        {
            // Where the "Go to my starting page" button goes (US51 rule).
            StartPage = _accounts.GetStartPage(User);

            RoleName = User.IsInRole(Roles.Admin) ? "Admin"
                     : User.IsInRole(Roles.Staff) ? "Staff"
                     : User.IsInRole(Roles.Customer) ? "Customer"
                     : "Guest";

            Email = User.Identity?.Name;

            // Which part of the system they tried to open, from the folder at the start of the address.
            var path = returnUrl ?? string.Empty;
            AreaName = path.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase) ? "Admin only"
                     : path.StartsWith("/Staff", StringComparison.OrdinalIgnoreCase) ? "Staff only"
                     : path.StartsWith("/Customer", StringComparison.OrdinalIgnoreCase) ? "Customers only"
                     : "Restricted";
        }
    }
}
