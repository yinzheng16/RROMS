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
    // US01 register, US02 login/logout. Later: US05 / US07 admin user management.
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
    }
}
