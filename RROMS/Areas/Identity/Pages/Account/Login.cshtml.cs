using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Areas.Identity.Pages.Account
{
    // US02 (Owner: Member 1): login for every role (Customer, Staff, Admin).
    // Replaces the built-in Identity Login page. The checks live in AccountService.
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        private readonly IAccountService _accounts;

        public LoginModel(IAccountService accounts)
        {
            _accounts = accounts;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ReturnUrl { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Please enter your email.")]
            [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
            [Display(Name = "Email")]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Please enter your password.")]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Remember me")]
            public bool RememberMe { get; set; }
        }

        public IActionResult OnGet(string? returnUrl = null)
        {
            // Already logged in: no need to log in again.
            if (User.Identity?.IsAuthenticated == true)
            {
                return LocalRedirect("~/");
            }

            ReturnUrl = returnUrl;

            // Coming back from a failed login (see OnPostAsync): show the message and keep the email.
            if (TempData["LoginError"] is string error)
            {
                ModelState.AddModelError(string.Empty, error);
                Input.Email = TempData["LoginEmail"] as string ?? string.Empty;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            ReturnUrl = returnUrl;

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _accounts.LoginAsync(Input.Email, Input.Password, Input.RememberMe);

            if (result == LoginResult.Success)
            {
                // Only go back to pages on our own site (stops "open redirect" tricks).
                // US51 will change this to send each role to its own starting page.
                var target = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "~/";
                return LocalRedirect(target);
            }

            TempData["LoginError"] = result switch
            {
                LoginResult.LockedOut => "Too many failed attempts. Your account is locked for 15 minutes. Please try again later.",
                LoginResult.Inactive => "This account has been deactivated. Please contact the restaurant.",
                _ => "Incorrect email or password."   // same message for a wrong email or a wrong password, on purpose
            };
            TempData["LoginEmail"] = Input.Email;   // the password is never kept

            // Post/Redirect/Get: go back to the normal Login page instead of showing it straight from the POST.
            // So pressing refresh just reloads the page; it never sends the password again
            // (no "Confirm Form Resubmission" box, and no extra failed attempt towards the lockout).
            return RedirectToPage(new { returnUrl });
        }
    }
}
