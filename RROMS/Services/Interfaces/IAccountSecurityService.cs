using System.Security.Claims;

namespace RROMS.Services.Interfaces
{
    // US03 / US04 (Owner: Member 1): Security on My profile — change password and change email.
    // Always works on the LOGGED-IN user. Every wrong password counts towards the normal login lockout
    // (5 tries -> locked for 15 minutes), so nobody can guess a password from this page either.

    // Field: which box the message belongs to ("CurrentPassword", "NewPassword", "ConfirmPassword", "NewEmail", "Password").
    // TriesLeft: shown as "3 tries left" after a wrong password. LockedMinutes: set when the account is locked.
    public record SecurityResult(bool Ok, string? Field = null, string? Message = null,
                                 int? TriesLeft = null, int? LockedMinutes = null, string? Email = null);

    public record EmailConfirmResult(bool Ok, string Message, string? NewEmail = null, bool SignedIn = false);

    // US52: is a reset link still usable? MaskedEmail = "c••••••r@rroms.com" to show whose password is being reset.
    public record ResetLinkInfo(bool Valid, string? MaskedEmail = null, string? Message = null);

    public interface IAccountSecurityService
    {
        // Checks the current password, applies the new one, keeps this device logged in,
        // and emails "Your password was changed" (with a reset link for "Wasn't you?").
        Task<SecurityResult> ChangePasswordAsync(ClaimsPrincipal user, string? currentPassword,
            string? newPassword, string? confirmPassword, string resetPasswordUrl);

        // Checks the password and sends a confirmation link to the NEW address.
        // Nothing changes until the link is clicked (valid 24 hours).
        // buildConfirmUrl(userId, newEmail, code) -> absolute link to /Identity/Account/ConfirmEmailChange.
        Task<SecurityResult> RequestEmailChangeAsync(ClaimsPrincipal user, string? newEmail, string? password,
            Func<string, string, string, string> buildConfirmUrl);

        // Called by the link: changes email + login name, and tells the OLD address about it.
        Task<EmailConfirmResult> ConfirmEmailChangeAsync(string? userId, string? email, string? code, ClaimsPrincipal visitor);

        // ---------- US52 Forgot password ----------

        // Sends a one-time reset link (valid 1 hour) if the email belongs to an active account.
        // Always answers the same way whether or not the email is registered, so nobody can find out who has an account.
        // buildResetUrl(userId, code) -> absolute link to /Identity/Account/ResetPassword.
        Task<SecurityResult> RequestPasswordResetAsync(string? email, Func<string, string, string> buildResetUrl);

        // Checks the link before showing the form (expired / already used / broken).
        Task<ResetLinkInfo> CheckResetLinkAsync(string? userId, string? code);

        // Sets the new password, clears any lockout and emails "Your password was changed".
        // forgotPasswordUrl goes in that email for "Wasn't you?".
        Task<SecurityResult> ResetPasswordAsync(string? userId, string? code, string? newPassword,
            string? confirmPassword, string forgotPasswordUrl);
    }
}
