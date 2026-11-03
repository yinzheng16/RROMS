using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RROMS.Areas.Identity.Pages.Account
{
    // US52 (Owner: Member 1): "Check your inbox". Replaces Identity's built-in page.
    [AllowAnonymous]
    public class ForgotPasswordConfirmationModel : PageModel
    {
        public string? SentTo { get; private set; }

        public void OnGet()
        {
            SentTo = TempData.Peek("ResetSentTo") as string;   // Peek: "Send again" still knows it after a refresh
        }
    }
}
