using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RROMS.Areas.Identity.Pages.Account
{
    // US52 (Owner: Member 1): "Password reset". Replaces Identity's built-in page.
    [AllowAnonymous]
    public class ResetPasswordConfirmationModel : PageModel
    {
        public string? For { get; private set; }

        // US05: first password for an account an Admin created ("Set your password").
        public bool Welcome { get; private set; }

        public void OnGet()
        {
            For = TempData["ResetDoneFor"] as string;
            Welcome = TempData["ResetWelcome"] as bool? ?? false;
        }
    }
}
