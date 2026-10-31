using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Areas.Identity.Pages.Account
{
    // US03 / US04 (Owner: Member 1): opened from the "Confirm my email" button in the email.
    // Replaces the built-in Identity page of the same name. Anyone with the link can open it
    // (e.g. on a phone where they are not logged in); the code in the link proves it is theirs.
    [AllowAnonymous]
    public class ConfirmEmailChangeModel : PageModel
    {
        private readonly IAccountSecurityService _security;

        public ConfirmEmailChangeModel(IAccountSecurityService security) => _security = security;

        public EmailConfirmResult Result { get; private set; } = new(false, "");

        public async Task<IActionResult> OnGetAsync(string? userId, string? email, string? code)
        {
            Result = await _security.ConfirmEmailChangeAsync(userId, email, code, User);
            return Page();
        }
    }
}
