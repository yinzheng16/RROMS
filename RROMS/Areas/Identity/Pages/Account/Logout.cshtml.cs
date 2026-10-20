using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Areas.Identity.Pages.Account
{
    // US02 (Owner: Member 1): log out. Replaces the built-in Identity Logout page.
    // Only works as a POST from the "Log out" button (with the anti-forgery token),
    // so a link on another website cannot log people out.
    [Authorize]
    public class LogoutModel : PageModel
    {
        private readonly IAccountService _accounts;

        public LogoutModel(IAccountService accounts)
        {
            _accounts = accounts;
        }

        // Someone typed /Identity/Account/Logout in the address bar: just go home.
        public IActionResult OnGet() => LocalRedirect("~/");

        public async Task<IActionResult> OnPostAsync()
        {
            await _accounts.LogoutAsync();   // removes the login cookie

            TempData["SuccessMessage"] = "You have been logged out. See you again soon!";
            return LocalRedirect("~/");
        }
    }
}
