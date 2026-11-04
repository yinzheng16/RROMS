using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using PhoneNumbers;   // NuGet: libphonenumber-csharp
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 1 (Identity, Access & AI Chatbot)
    public class AccountService : IAccountService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        // Google's phone number rules for every country (thread-safe, create once).
        private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

        public AccountService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public async Task<IdentityResult> RegisterCustomerAsync(string fullName, string email, string phoneNumber, string password)
        {
            var cleanEmail = email.Trim();

            var user = new ApplicationUser
            {
                UserName = cleanEmail,               // users log in with their email
                Email = cleanEmail,
                FullName = fullName.Trim(),
                PhoneNumber = phoneNumber,          // already checked and formatted by FormatPhoneNumber
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            // Identity hashes the password and checks: unique email, password rules.
            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                return result;
            }

            // The role is fixed here, not taken from the form, so nobody can register as Staff or Admin.
            var roleResult = await _userManager.AddToRoleAsync(user, Roles.Customer);
            if (!roleResult.Succeeded)
            {
                // Never leave an account without a role.
                await _userManager.DeleteAsync(user);
                return roleResult;
            }

            return IdentityResult.Success;
        }

        public string? FormatPhoneNumber(string countryIso, string phoneNumber)
        {
            try
            {
                // Understands local formats too: "012-345 6789" for MY drops the leading 0.
                var parsed = PhoneUtil.Parse(phoneNumber, countryIso);

                // Must be a real number for THAT country (right length and starting digits).
                if (!PhoneUtil.IsValidNumberForRegion(parsed, countryIso))
                {
                    return null;
                }

                return PhoneUtil.Format(parsed, PhoneNumberFormat.E164);   // "+60123456789"
            }
            catch (NumberParseException)
            {
                return null;   // not a phone number at all
            }
        }

        // ---------- US02: login / logout ----------

        public async Task<LoginResult> LoginAsync(string email, string password, bool rememberMe)
        {
            var user = await _userManager.FindByEmailAsync(email.Trim());
            if (user is null)
            {
                return LoginResult.InvalidCredentials;   // same message as a wrong password, so nobody can test which emails exist
            }

            // Checks the hashed password. lockoutOnFailure: true -> every wrong password counts,
            // and after 5 in a row (Program.cs) the account is locked for 15 minutes.
            var check = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

            if (check.IsLockedOut)
            {
                return LoginResult.LockedOut;
            }
            if (!check.Succeeded)
            {
                return LoginResult.InvalidCredentials;
            }

            // Right password, but an Admin has deactivated this account (US07).
            if (!user.IsActive)
            {
                return LoginResult.Inactive;
            }

            // "Keep me logged in" only works for customers (their own phone or laptop).
            // Staff and Admin may use a shared restaurant computer, so they never stay logged in.
            var isCustomer = await _userManager.IsInRoleAsync(user, Roles.Customer);

            // Creates the login cookie.
            if (rememberMe && isCustomer)
            {
                // Customer ticked the box: survives closing the browser, for 7 days (restarts on each visit).
                await _signInManager.SignInAsync(user, new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                });
            }
            else
            {
                // Not ticked, or Staff / Admin: gone when the browser closes,
                // and after 30 minutes of doing nothing (Program.cs).
                await _signInManager.SignInAsync(user, isPersistent: false);
            }
            return LoginResult.Success;
        }

        public async Task LogoutAsync()
        {
            await _signInManager.SignOutAsync();
        }

        // ---------- US51: starting page per role ----------

        // The only place that decides where each role starts. Pages are built by:
        // Admin overview = Member 5 (US41), Staff reservations = Member 2 (US16), Home = Member 1 (US50).
        private const string AdminStartPage = "/Admin";
        private const string StaffStartPage = "/Staff/Reservations";   // US16 page (Member 2); there is no /Staff index
        private const string CustomerStartPage = "/";

        public async Task<string> GetStartPageAsync(string email)
        {
            var user = await _userManager.FindByEmailAsync(email.Trim());
            if (user is null) return CustomerStartPage;

            if (await _userManager.IsInRoleAsync(user, Roles.Admin)) return AdminStartPage;
            if (await _userManager.IsInRoleAsync(user, Roles.Staff)) return StaffStartPage;
            return CustomerStartPage;
        }

        // US50: full name for "Welcome back, ..." on the Home page.
        // Full name, not the first word, because Malaysian names often start with the surname.
        public async Task<string?> GetDisplayNameAsync(ClaimsPrincipal user)
        {
            if (user.Identity?.IsAuthenticated != true) return null;
            var appUser = await _userManager.GetUserAsync(user);
            var fullName = appUser?.FullName?.Trim();
            return string.IsNullOrEmpty(fullName) ? null : fullName;
        }

        public string GetStartPage(ClaimsPrincipal user)
        {
            if (user.IsInRole(Roles.Admin)) return AdminStartPage;
            if (user.IsInRole(Roles.Staff)) return StaffStartPage;
            return CustomerStartPage;
        }
    }
}
