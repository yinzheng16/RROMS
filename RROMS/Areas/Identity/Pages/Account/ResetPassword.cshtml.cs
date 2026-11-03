using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Areas.Identity.Pages.Account
{
    // US52 (Owner: Member 1): opened from "Choose a new password" in the email.
    // Replaces Identity's built-in page. The link carries the user id + one-time code (no email in the URL).
    // US05: also opened from "Set my password" in the welcome email (welcome=true only changes the wording).
    [AllowAnonymous]
    public class ResetPasswordModel : PageModel
    {
        private readonly IAccountSecurityService _security;
        private readonly SignInManager<ApplicationUser> _signIn;

        public ResetPasswordModel(IAccountSecurityService security, SignInManager<ApplicationUser> signIn)
        {
            _security = security;
            _signIn = signIn;
        }

        [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
        [BindProperty(SupportsGet = true)] public string? Code { get; set; }
        [BindProperty(SupportsGet = true)] public bool Welcome { get; set; }
        [BindProperty] public string? NewPassword { get; set; }
        [BindProperty] public string? ConfirmPassword { get; set; }

        public ResetLinkInfo Link { get; private set; } = new(false);

        public async Task OnGetAsync()
        {
            Link = await _security.CheckResetLinkAsync(UserId, Code);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var resetUrl = Url.Page("/Account/ForgotPassword", pageHandler: null,
                               values: new { area = "Identity" }, protocol: Request.Scheme)
                           ?? $"{Request.Scheme}://{Request.Host}/Identity/Account/ForgotPassword";

            var result = await _security.ResetPasswordAsync(UserId, Code, NewPassword, ConfirmPassword, resetUrl);
            if (!result.Ok)
            {
                Link = await _security.CheckResetLinkAsync(UserId, Code);
                if (result.Field is not null) ModelState.AddModelError(result.Field, result.Message ?? "Please check this field.");
                else Link = Link with { Valid = false, Message = result.Message };
                NewPassword = ConfirmPassword = null;   // never send passwords back to the page
                return Page();
            }

            // Someone already logged in on this browser: log them out, so the next login uses the new password.
            if (_signIn.IsSignedIn(User)) await _signIn.SignOutAsync();

            TempData["ResetDoneFor"] = result.Email;
            TempData["ResetWelcome"] = Welcome;
            return RedirectToPage("./ResetPasswordConfirmation");
        }

        public string? ErrorFor(string field) => ModelState[field]?.Errors.FirstOrDefault()?.ErrorMessage;
    }
}
