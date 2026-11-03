using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace RROMS.Services
{
    // US52 (Owner: Member 1): password-reset links get their OWN token provider,
    // so they expire after 1 hour while change-email links (US03) keep the normal 24 hours.
    // A link works once: resetting the password changes the security stamp, which cancels the link.
    public class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
    {
        public const string ProviderName = "PasswordReset";

        public PasswordResetTokenProviderOptions()
        {
            Name = ProviderName;
            TokenLifespan = TimeSpan.FromHours(1);
        }
    }

    public class PasswordResetTokenProvider<TUser> : DataProtectorTokenProvider<TUser> where TUser : class
    {
        public PasswordResetTokenProvider(IDataProtectionProvider dataProtectionProvider,
            IOptions<PasswordResetTokenProviderOptions> options,
            ILogger<DataProtectorTokenProvider<TUser>> logger)
            : base(dataProtectionProvider, options, logger)
        {
        }
    }
}
