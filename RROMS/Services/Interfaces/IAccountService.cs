using Microsoft.AspNetCore.Identity;

namespace RROMS.Services.Interfaces
{
    // Owner: Member 1 (Identity, Access & AI Chatbot)
    // Business logic for user accounts. Pages call this service, never UserManager directly.
    // US01: customer self-registration. Later: US05 / US07 admin user management.
    public interface IAccountService
    {
        // Creates a new account with the Customer role.
        // Public registration can only ever create Customers; Staff and Admin accounts are created by an Admin (US05).
        Task<IdentityResult> RegisterCustomerAsync(string fullName, string email, string phoneNumber, string password);

        // Checks a phone number against the rules of the chosen country (e.g. "MY").
        // Returns it in international format ("+60123456789"), or null if it is not a valid number for that country.
        string? FormatPhoneNumber(string countryIso, string phoneNumber);
    }
}
