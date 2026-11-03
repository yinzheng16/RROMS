using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // US03 / US04 (Owner: Member 1): change password and change email for the logged-in user.
    // Emails go through the shared sender (EN07). A failed email never undoes a saved change.
    public class AccountSecurityService : IAccountSecurityService
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly IEmailSender _email;
        private readonly IHomeService _home;
        private readonly IdentityOptions _identity;
        private readonly ILogger<AccountSecurityService> _logger;

        public AccountSecurityService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
            IEmailSender email, IHomeService home, IOptions<IdentityOptions> identity, ILogger<AccountSecurityService> logger)
        {
            _users = users;
            _signIn = signIn;
            _email = email;
            _home = home;
            _identity = identity.Value;
            _logger = logger;
        }

        // ================= Change password =================

        public async Task<SecurityResult> ChangePasswordAsync(ClaimsPrincipal principal, string? currentPassword,
            string? newPassword, string? confirmPassword, string resetPasswordUrl)
        {
            var user = await _users.GetUserAsync(principal);
            if (user is null) return new(false, Message: "Please log in again.");

            var locked = await LockedAsync(user);
            if (locked is not null) return locked;

            if (string.IsNullOrEmpty(currentPassword)) return new(false, "CurrentPassword", "Please enter your current password.");
            if (string.IsNullOrEmpty(newPassword)) return new(false, "NewPassword", "Please enter a new password.");
            if (newPassword != confirmPassword) return new(false, "ConfirmPassword", "The two new passwords don't match.");

            // Wrong current password -> counts like a wrong login (5 tries -> 15 minutes).
            var wrong = await CheckPasswordAsync(user, currentPassword, "CurrentPassword", "That's not your current password.");
            if (wrong is not null) return wrong;

            if (newPassword == currentPassword)
                return new(false, "NewPassword", "Choose a password that is different from your current one.");

            var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!result.Succeeded)
                return new(false, "NewPassword", string.Join(" ", result.Errors.Select(e => e.Description)));

            await _users.ResetAccessFailedCountAsync(user);
            await _signIn.RefreshSignInAsync(user);   // the security stamp changed; keep THIS device logged in

            await SendPasswordChangedAsync(user, resetPasswordUrl);

            _logger.LogInformation("Password changed for user {UserId}.", user.Id);
            return new(true, Email: EmailTemplates.Mask(user.Email));
        }

        // ================= Change email: step 1, send the link =================

        public async Task<SecurityResult> RequestEmailChangeAsync(ClaimsPrincipal principal, string? newEmail, string? password,
            Func<string, string, string, string> buildConfirmUrl)
        {
            var user = await _users.GetUserAsync(principal);
            if (user is null) return new(false, Message: "Please log in again.");

            var locked = await LockedAsync(user);
            if (locked is not null) return locked;

            newEmail = newEmail?.Trim() ?? "";
            if (newEmail.Length == 0) return new(false, "NewEmail", "Please enter your new email.");
            if (newEmail.Length > 256 || !new EmailAddressAttribute().IsValid(newEmail))
                return new(false, "NewEmail", "Please enter a valid email address, e.g. name@example.com.");
            if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
                return new(false, "NewEmail", "That's already your email.");
            if (string.IsNullOrEmpty(password)) return new(false, "Password", "Please enter your password.");

            var wrong = await CheckPasswordAsync(user, password, "Password", "That's not your password.");
            if (wrong is not null) return wrong;

            // The user is logged in and has just proved the password, so saying "already used" is safe here.
            var owner = await _users.FindByEmailAsync(newEmail);
            if (owner is not null && owner.Id != user.Id)
                return new(false, "NewEmail", "This email is already used by another account.");

            await _users.ResetAccessFailedCountAsync(user);

            var token = await _users.GenerateChangeEmailTokenAsync(user, newEmail);   // valid 24 hours (Identity default)
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = buildConfirmUrl(user.Id, newEmail, code);

            await SendAsync(newEmail, "Confirm your new email for YORU", EmailTemplates.Build(
                kicker: "One last step",
                title: "Confirm your new email",
                name: user.FullName,
                introHtml: "Click below to start using this address for your YORU account.",
                rows: new[] { new EmailTemplates.Row("New email", newEmail), new EmailTemplates.Row("Link works for", "24 hours") },
                buttonText: "Confirm my email",
                buttonUrl: link,
                smallText: "Didn't ask for this? Ignore this email and nothing changes.",
                footerLine: await FooterAsync()));

            return new(true, Email: newEmail);
        }

        // ================= Change email: step 2, the link was clicked =================

        public async Task<EmailConfirmResult> ConfirmEmailChangeAsync(string? userId, string? email, string? code, ClaimsPrincipal visitor)
        {
            const string expired = "This link has expired or was already used. You can ask for a new one on My profile.";
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(code))
                return new(false, "This confirmation link is not complete. Please use the button in the email.");

            var user = await _users.FindByIdAsync(userId);
            if (user is null) return new(false, expired);

            // Link clicked twice: already done.
            if (string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                return new(true, "Your account already uses this email.", email, IsSameUser(visitor, user));

            string token;
            try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
            catch (FormatException) { return new(false, expired); }

            var owner = await _users.FindByEmailAsync(email);
            if (owner is not null && owner.Id != user.Id)
                return new(false, "This email is now used by another account, so it can't be added to yours.");

            var oldEmail = user.Email;
            var changed = await _users.ChangeEmailAsync(user, email, token);   // also marks the email as confirmed
            if (!changed.Succeeded) return new(false, expired);

            // Users log in with their email, so the login name changes too.
            var renamed = await _users.SetUserNameAsync(user, email);
            if (!renamed.Succeeded)
            {
                _logger.LogError("Email changed but user name not updated for {UserId}: {Errors}", user.Id,
                    string.Join(" ", renamed.Errors.Select(e => e.Description)));
            }

            var signedIn = IsSameUser(visitor, user);
            if (signedIn) await _signIn.RefreshSignInAsync(user);

            if (!string.IsNullOrEmpty(oldEmail))
            {
                var phone = (await SafeHomeAsync())?.Phone;
                await SendAsync(oldEmail, "Your YORU login email was changed", EmailTemplates.Build(
                    kicker: "Account security",
                    title: "Your login email was changed",
                    name: user.FullName,
                    introHtml: "Your YORU account now logs in with a new address.",
                    rows: new[]
                    {
                        new EmailTemplates.Row("Old", oldEmail),
                        new EmailTemplates.Row("New", email),
                        new EmailTemplates.Row("When", Now())
                    },
                    warnHtml: string.IsNullOrEmpty(phone)
                        ? "<b>Wasn't you?</b> Contact us straight away and we'll secure your account."
                        : $"<b>Wasn't you?</b> Call us at {EmailTemplates.Encode(phone)} and we'll secure your account.",
                    smallText: "Sent to your old address so you know about the change.",
                    footerLine: await FooterAsync()));
            }

            _logger.LogInformation("Email changed for user {UserId}.", user.Id);
            return new(true, "Your account now uses this email.", email, signedIn);
        }

        // ================= US52 Forgot password =================

        private const string ResetLinkExpired =
            "This reset link has expired or was already used. Links work once, for 1 hour. Please ask for a new one.";

        public async Task<SecurityResult> RequestPasswordResetAsync(string? email, Func<string, string, string> buildResetUrl)
        {
            email = email?.Trim() ?? "";
            if (email.Length == 0) return new(false, "Email", "Please enter your email.");
            if (email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
                return new(false, "Email", "Please enter a valid email address, e.g. name@example.com.");

            var user = await _users.FindByEmailAsync(email);

            // Unknown or deactivated account: send nothing, but answer exactly like a real one.
            if (user is null || !user.IsActive || string.IsNullOrEmpty(user.Email))
            {
                _logger.LogInformation("Password reset asked for an unknown or inactive email.");
                return new(true, Email: email);
            }

            var token = await _users.GeneratePasswordResetTokenAsync(user);   // PasswordReset provider: 1 hour
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = buildResetUrl(user.Id, code);

            await SendAsync(user.Email, "Reset your YORU password", EmailTemplates.Build(
                kicker: "Password reset",
                title: "Choose a new password",
                name: user.FullName,
                introHtml: "We got a request to reset the password for your YORU account. Click below to choose a new one.",
                rows: new[]
                {
                    new EmailTemplates.Row("Account", user.Email),
                    new EmailTemplates.Row("Link works for", "1 hour, once")
                },
                buttonText: "Choose a new password",
                buttonUrl: link,
                smallText: "Didn't ask for this? Ignore this email and your password stays the same.",
                footerLine: await FooterAsync()));

            _logger.LogInformation("Password reset link sent to user {UserId}.", user.Id);
            return new(true, Email: email);
        }

        public async Task<ResetLinkInfo> CheckResetLinkAsync(string? userId, string? code)
        {
            var (user, token) = await ReadResetLinkAsync(userId, code);
            if (user is null || token is null) return new(false, Message: ResetLinkExpired);

            var ok = await _users.VerifyUserTokenAsync(user, _users.Options.Tokens.PasswordResetTokenProvider,
                UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token);
            return ok ? new(true, EmailTemplates.Mask(user.Email)) : new(false, Message: ResetLinkExpired);
        }

        public async Task<SecurityResult> ResetPasswordAsync(string? userId, string? code, string? newPassword,
            string? confirmPassword, string forgotPasswordUrl)
        {
            var (user, token) = await ReadResetLinkAsync(userId, code);
            if (user is null || token is null) return new(false, Message: ResetLinkExpired);

            if (string.IsNullOrEmpty(newPassword)) return new(false, "NewPassword", "Please enter a new password.");
            if (newPassword != confirmPassword) return new(false, "ConfirmPassword", "The two new passwords don't match.");

            // US05: an account created by an Admin has no password until this link is used.
            var firstPassword = !await _users.HasPasswordAsync(user);

            if (!firstPassword && await _users.CheckPasswordAsync(user, newPassword))
                return new(false, "NewPassword", "Choose a password that is different from your old one.");

            var result = await _users.ResetPasswordAsync(user, token, newPassword);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(e => e.Code == "InvalidToken")) return new(false, Message: ResetLinkExpired);
                return new(false, "NewPassword", string.Join(" ", result.Errors.Select(e => e.Description)));
            }

            // A locked account (5 wrong tries) can log in again straight away with the new password.
            await _users.ResetAccessFailedCountAsync(user);
            await _users.SetLockoutEndDateAsync(user, null);

            // "Your password was changed" only makes sense when there was a password before.
            if (!firstPassword) await SendPasswordChangedAsync(user, forgotPasswordUrl);

            if (firstPassword) _logger.LogInformation("First password set for user {UserId}.", user.Id);
            else _logger.LogInformation("Password reset for user {UserId}.", user.Id);
            return new(true, Email: EmailTemplates.Mask(user.Email));
        }

        private async Task<(ApplicationUser? User, string? Token)> ReadResetLinkAsync(string? userId, string? code)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code)) return (null, null);
            var user = await _users.FindByIdAsync(userId);
            if (user is null || !user.IsActive) return (null, null);
            try { return (user, Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code))); }
            catch (FormatException) { return (null, null); }
        }

        // ================= helpers =================

        // "Your password was changed" (after Change password on My profile, or after a reset).
        private async Task SendPasswordChangedAsync(ApplicationUser user, string resetPasswordUrl)
        {
            if (string.IsNullOrEmpty(user.Email)) return;
            await SendAsync(user.Email, "Your YORU password was changed", EmailTemplates.Build(
                kicker: "Account security",
                title: "Your password was changed",
                name: user.FullName,
                introHtml: "The password for your YORU account was just changed. If this was you, there's nothing else to do.",
                rows: new[] { new EmailTemplates.Row("When", Now()), new EmailTemplates.Row("Account", user.Email) },
                warnHtml: "<b>Wasn't you?</b> Reset your password now so nobody else can use your account.",
                buttonText: "Reset my password",
                buttonUrl: resetPasswordUrl,
                footerLine: await FooterAsync()));
        }

        // Returns null when the password is right; otherwise counts the failure like a wrong login.
        private async Task<SecurityResult?> CheckPasswordAsync(ApplicationUser user, string password, string field, string message)
        {
            if (await _users.CheckPasswordAsync(user, password)) return null;

            await _users.AccessFailedAsync(user);
            var locked = await LockedAsync(user);
            if (locked is not null) return locked;

            var left = Math.Max(0, _identity.Lockout.MaxFailedAccessAttempts - await _users.GetAccessFailedCountAsync(user));
            return new(false, field, message, TriesLeft: left);
        }

        private async Task<SecurityResult?> LockedAsync(ApplicationUser user)
        {
            if (!await _users.IsLockedOutAsync(user)) return null;
            var end = await _users.GetLockoutEndDateAsync(user);
            var minutes = end.HasValue ? Math.Max(1, (int)Math.Ceiling((end.Value - DateTimeOffset.UtcNow).TotalMinutes)) : 15;
            return new(false, "CurrentPassword",
                $"Too many wrong passwords. Your account is locked for {minutes} minute{(minutes == 1 ? "" : "s")}. Please try again later.",
                TriesLeft: 0, LockedMinutes: minutes);
        }

        private static bool IsSameUser(ClaimsPrincipal visitor, ApplicationUser user) =>
            visitor.Identity?.IsAuthenticated == true &&
            visitor.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id;

        private static string Now() =>
            DateTime.Now.ToString("ddd, d MMM yyyy · h:mm tt", CultureInfo.InvariantCulture);

        // The shared sender never throws, but stay safe: a failed email must not undo the change.
        private async Task SendAsync(string to, string subject, string html)
        {
            try { await _email.SendEmailAsync(to, subject, html); }
            catch (Exception ex) { _logger.LogError(ex, "Security email to {To} failed.", to); }
        }

        private async Task<HomeInfo?> SafeHomeAsync()
        {
            try { return await _home.GetInfoAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not read restaurant details for the email footer."); return null; }
        }

        private async Task<string> FooterAsync()
        {
            var info = await SafeHomeAsync();
            if (info is null) return Brand.FullName;
            var address = info.Address.Replace("\r", "").Replace("\n", ", ");
            return $"{Brand.FullName} · {address} · {info.Phone}";
        }
    }
}
