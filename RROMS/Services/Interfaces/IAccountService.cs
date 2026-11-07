using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace RROMS.Services.Interfaces
{
    // What happened when someone tried to log in (US02).
    public enum LoginResult
    {
        Success,
        InvalidCredentials,   // wrong email or password (we never say which one)
        LockedOut,            // 5 wrong passwords in a row -> locked for 15 minutes
        Inactive              // account deactivated by an Admin (US07)
    }

    // Owner: Member 1 (Identity, Access & AI Chatbot)
    // Business logic for user accounts. Pages call this service, never UserManager / SignInManager directly.
    // US01 register, US02 login/logout, US51 starting page per role. Later: US05 / US07 admin user management.
    public interface IAccountService
    {
        // Creates a new account with the Customer role.
        // Public registration can only ever create Customers; Staff and Admin accounts are created by an Admin (US05).
        Task<IdentityResult> RegisterCustomerAsync(string fullName, string email, string phoneNumber, string password);

        // Checks a phone number against the rules of the chosen country (e.g. "MY").
        // Returns it in international format ("+60123456789"), or null if it is not a valid number for that country.
        string? FormatPhoneNumber(string countryIso, string phoneNumber);

        // US02: checks the password (counts failed attempts for lockout) and signs the user in.
        Task<LoginResult> LoginAsync(string email, string password, bool rememberMe);

        // US02: signs the user out and removes the login cookie.
        Task LogoutAsync();

        // US51: each role's starting page. Admin -> "/Admin" (US41), Staff -> "/Staff/Reservations" (US16), Customer -> "/" (US50).
        // Use the email version right after login (the new login cookie is only read on the next request).
        Task<string> GetStartPageAsync(string email);

        // US51: same, for someone who is already logged in (e.g. opens the Login page again).
        string GetStartPage(ClaimsPrincipal user);

        Task<string?> GetDisplayNameAsync(ClaimsPrincipal user);
    }
}
