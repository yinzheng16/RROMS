using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Areas.Identity.Pages.Account
{
    // US52 (Owner: Member 1): "Forgot password?" on Login (and "Forgot it?" on My profile) open this page.
    // Replaces Identity's built-in page. Same answer whether or not the email is registered.
    [AllowAnonymous]
    public class ForgotPasswordModel : PageModel
    {
        private readonly IAccountSecurityService _security;
        private readonly UserManager<ApplicationUser> _users;

        public ForgotPasswordModel(IAccountSecurityService security, UserManager<ApplicationUser> users)
        {
            _security = security;
            _users = users;
        }

        [BindProperty]
        public string? Email { get; set; }

        public async Task OnGetAsync()
        {
            // Logged in (came from My profile): fill in their email.
            if (User.Identity?.IsAuthenticated == true)
                Email = (await _users.GetUserAsync(User))?.Email;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var result = await _security.RequestPasswordResetAsync(Email,
                (userId, code) => Url.Page("/Account/ResetPassword", pageHandler: null,
                    values: new { area = "Identity", userId, code }, protocol: Request.Scheme)!);

            if (!result.Ok)
            {
                ModelState.AddModelError(nameof(Email), result.Message ?? "Please check your email.");
                return Page();
            }

            // Kept in TempData (a cookie), never in the URL.
            TempData["ResetSentTo"] = result.Email;
            return RedirectToPage("./ForgotPasswordConfirmation");
        }
    }
}
