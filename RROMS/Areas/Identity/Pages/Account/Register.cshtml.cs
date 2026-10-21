using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Areas.Identity.Pages.Account
{
    // US01 (Owner: Member 1): public customer registration.
    // Replaces the built-in Identity Register page. Always creates a Customer;
    // Staff and Admin accounts are created by an Admin (US05).
    [AllowAnonymous]
    public class RegisterModel : PageModel
    {
        private readonly IAccountService _accounts;

        public RegisterModel(IAccountService accounts)
        {
            _accounts = accounts;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ReturnUrl { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Please enter your full name.")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Full name must be {2} to {1} characters.")]
            [Display(Name = "Full name")]
            public string FullName { get; set; } = string.Empty;

            [Required(ErrorMessage = "Please enter your email.")]
            [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
            [StringLength(100)]
            [Display(Name = "Email")]
            public string Email { get; set; } = string.Empty;

            // Country picked from the dropdown, e.g. "MY". Malaysia is selected by default.
            [Required(ErrorMessage = "Please choose your country code.")]
            [Display(Name = "Country code")]
            public string CountryCode { get; set; } = "MY";

            // The number without the country code: 6 to 15 digits, spaces and dashes allowed.
            [Required(ErrorMessage = "Please enter your phone number.")]
            [RegularExpression(@"^[0-9][0-9\s-]{4,18}[0-9]$",
                ErrorMessage = "Please enter a valid phone number using numbers only, e.g. 12-345 6789.")]
            [Display(Name = "Phone number")]
            public string PhoneNumber { get; set; } = string.Empty;

            // Same rule as the hint under the box. Checked in the browser and again on the server.
            // Under 8 characters  -> only "must be at least 8 characters" shows (StringLength).
            // 8 or more but weak  -> only the uppercase/lowercase/number/symbol message shows.
            // (The ".{0,7}|" part lets short passwords pass this check, so the two messages never mix.)
            [Required(ErrorMessage = "Please enter a password.")]
            [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least {2} characters.")]
            [RegularExpression(@"^(.{0,7}|(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).*)$",
                ErrorMessage = "Password needs an uppercase letter, a lowercase letter, a number and a symbol.")]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; } = string.Empty;

            [Required(ErrorMessage = "Please confirm your password.")]
            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        public IActionResult OnGet(string? returnUrl = null)
        {
            // Someone already logged in doesn't need to register: go to their role's starting page (US51).
            if (User.Identity?.IsAuthenticated == true)
            {
                return LocalRedirect(_accounts.GetStartPage(User));
            }

            ReturnUrl = returnUrl;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            ReturnUrl = returnUrl;

            // The country must be one from our list (stops tampered form posts).
            var country = PhoneCountries.Find(Input.CountryCode);
            if (country is null)
            {
                ModelState.AddModelError("Input.CountryCode", "Please choose your country code.");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Check the number against the chosen country's real rules (Google libphonenumber).
            // e.g. MY + "012-345 6789" -> "+60123456789"; MY + "55 555" -> not valid.
            var fullPhone = _accounts.FormatPhoneNumber(country!.Iso, Input.PhoneNumber);
            if (fullPhone is null)
            {
                ModelState.AddModelError("Input.PhoneNumber", $"That doesn't look like a valid {country.Name} phone number.");
                return Page();
            }

            var result = await _accounts.RegisterCustomerAsync(
                Input.FullName, Input.Email, fullPhone, Input.Password);

            if (result.Succeeded)
            {
                // Shown at the top of the Login page by the shared layout.
                TempData["SuccessMessage"] = "Your account has been created. Please log in.";
                return RedirectToPage("./Login", new { returnUrl });
            }

            // Show Identity's errors in plain words.
            var duplicateShown = false;
            foreach (var error in result.Errors)
            {
                if (error.Code is "DuplicateEmail" or "DuplicateUserName")
                {
                    if (!duplicateShown)
                    {
                        ModelState.AddModelError("Input.Email", "This email is already registered. Try logging in instead.");
                        duplicateShown = true;
                    }
                    continue;
                }

                // Password rule errors go under the password box, the rest at the top.
                var key = error.Code.StartsWith("Password") ? "Input.Password" : string.Empty;
                ModelState.AddModelError(key, error.Description);
            }

            return Page();
        }
    }
}
