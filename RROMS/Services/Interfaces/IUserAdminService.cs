using System.Security.Claims;
using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // ---------- US05 / US07 (Owner: Member 1): Admin → Users ----------

    // Search box, role chip, status switch and page number from the Users page.
    // Role: "Admin" / "Staff" / "Customer" / null = all. Status: "active" / "inactive" / null = any.
    // FocusUserId: open the page that contains this user (e.g. the account just added) instead of Page.
    public record UserListQuery(string? Search, string? Role, string? Status, int Page, string? FocusUserId = null);

    public record UserRow(
        string Id, string FullName, string Email, string? Phone, string Role,
        bool IsActive, DateTime CreatedAt, DateTime? InactiveSince,
        bool HasPassword,   // false = created by an Admin and the set-password link hasn't been used yet
        bool IsMe);         // the Admin looking at the page (can't change own role, deactivate or remove self)

    // Removed accounts are not counted anywhere.
    public record UserStats(int All, int Admins, int Staff, int Customers, int Inactive, int ActiveAdmins)
    {
        public int StaffAndAdmins => Admins + Staff;
    }

    public record UserLogRow(string TargetName, UserAdminAction Action, string? Detail, string DoneByName, DateTime DoneAt);

    public record UserListPage(
        IReadOnlyList<UserRow> Rows, int Matches, int Page, int PageCount, int PageSize,
        UserStats Stats, IReadOnlyList<UserLogRow> Log, int LogTotal, int MaxActiveAdmins);

    // Full history page (Admin → Users → Activity).
    // Counts: how many of each action in the whole log (for the summary bar), Total: all entries.
    public record UserLogPage(IReadOnlyList<UserLogRow> Rows, int Matches, int Page, int PageCount, int PageSize,
        IReadOnlyDictionary<UserAdminAction, int> Counts, int Total);

    // "Add user" pop-up. Phone is optional; CountryIso is e.g. "MY".
    // MyPassword: the Admin's own password, needed only when the new account is an Admin.
    public record NewUserInput(string? FullName, string? Email, string? CountryIso, string? Phone, string? Role, string? MyPassword);

    // Field: which box the message belongs to ("FullName", "Email", "Phone", "Role", "MyPassword"), or null for a toast.
    // UserId: the account that was created (to highlight it in the list).
    public record UserAdminResult(bool Ok, string Message, string? Field = null, string? UserId = null);

    // buildPasswordLink(userId, code, welcome) -> absolute link to /Identity/Account/ResetPassword.
    // welcome = true for a new account that has no password yet ("Set your password").
    public interface IUserAdminService
    {
        // US07: the list, the counts at the top and the latest activity.
        Task<UserListPage> GetUsersAsync(ClaimsPrincipal admin, UserListQuery query);

        // US07: every change, newest first. Action: null = all.
        Task<UserLogPage> GetActivityAsync(string? search, UserAdminAction? action, int page);

        // US05: creates the account WITHOUT a password and emails a one-time set-password link (1 hour).
        // New Admin: needs the Admin's own password, at most MaxActiveAdmins, and every Admin gets an email.
        Task<UserAdminResult> CreateUserAsync(ClaimsPrincipal admin, NewUserInput input,
            Func<string, string, bool, string> buildPasswordLink);

        // US05: Admin / Staff / Customer. Not your own role; always at least one active Admin.
        // Making someone Admin has the same rules as creating one (password, limit, email).
        Task<UserAdminResult> ChangeRoleAsync(ClaimsPrincipal admin, string? userId, string? role, string? myPassword);

        // US07: deactivate = can't log in, and is logged out everywhere within about a minute.
        // Reactivating an Admin counts towards the Admin limit.
        Task<UserAdminResult> SetActiveAsync(ClaimsPrincipal admin, string? userId, bool active);

        // US07: emails a reset link (or the set-password link again if they never set one). Active users only.
        Task<UserAdminResult> SendPasswordLinkAsync(ClaimsPrincipal admin, string? userId,
            Func<string, string, bool, string> buildPasswordLink);

        // US07: removes a deactivated Staff / Admin account (never a Customer, never yourself).
        // The name stays in old bills and logs; email, phone and password are wiped so the email can be used again.
        Task<UserAdminResult> RemoveUserAsync(ClaimsPrincipal admin, string? userId);
    }
}
