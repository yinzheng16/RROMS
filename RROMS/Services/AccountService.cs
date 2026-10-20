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

        // Google's phone number rules for every country (thread-safe, create once).
        private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

        public AccountService(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
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
    }
}
