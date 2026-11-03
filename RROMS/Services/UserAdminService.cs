using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // US05 / US07 (Owner: Member 1): Admin → Users.
    // US05: create Staff / Admin / Customer accounts and set roles.
    // US07: search, filter, deactivate / reactivate, send a password link, remove Staff / Admin accounts,
    //       and keep a dated log of every change.
    // Admin accounts are protected: re-type your password, at most MaxActiveAdmins, and every Admin is emailed.
    // Nothing is really deleted: orders, bills, payments and logs point to accounts. "Remove" hides the account
    // and wipes its email, phone and password instead.
    public class UserAdminService : IUserAdminService
    {
        public const int PageSize = 10;
        public const int LogPageSize = 10;   // same as the Users list
        public const int MaxActiveAdmins = 3;   // change here if the restaurant needs more

        private static readonly string[] AllRoles = { Roles.Admin, Roles.Staff, Roles.Customer };

        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IAccountService _accounts;
        private readonly IEmailSender _email;
        private readonly IHomeService _home;
        private readonly ILogger<UserAdminService> _logger;

        public UserAdminService(ApplicationDbContext db, UserManager<ApplicationUser> users, IAccountService accounts,
            IEmailSender email, IHomeService home, ILogger<UserAdminService> logger)
        {
            _db = db;
            _users = users;
            _accounts = accounts;
            _email = email;
            _home = home;
            _logger = logger;
        }

        // ================= US07: list, counts, activity =================

        public async Task<UserListPage> GetUsersAsync(ClaimsPrincipal admin, UserListQuery query)
        {
            var meId = _users.GetUserId(admin);

            // Each user has one role. No role (should not happen) is treated as Customer.
            var roleNames = from ur in _db.UserRoles
                            join r in _db.Roles on ur.RoleId equals r.Id
                            select new { ur.UserId, r.Name };

            var all = _db.Users.AsNoTracking()
                .Where(u => u.RemovedAt == null)
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.Email,
                    Phone = u.PhoneNumber,
                    u.IsActive,
                    u.CreatedAt,
                    HasPassword = u.PasswordHash != null,
                    Role = roleNames.Where(x => x.UserId == u.Id).Select(x => x.Name).FirstOrDefault() ?? Roles.Customer
                });

            var stats = new UserStats(
                All: await all.CountAsync(),
                Admins: await all.CountAsync(x => x.Role == Roles.Admin),
                Staff: await all.CountAsync(x => x.Role == Roles.Staff),
                Customers: await all.CountAsync(x => x.Role == Roles.Customer),
                Inactive: await all.CountAsync(x => !x.IsActive),
                ActiveAdmins: await all.CountAsync(x => x.Role == Roles.Admin && x.IsActive));

            var filtered = all;

            var search = query.Search?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                // "012-345 6789" finds "+60123456789": compare digits only, without the leading 0.
                var digits = new string(search.Where(char.IsDigit).ToArray()).TrimStart('0');
                var byPhone = digits.Length >= 3;
                filtered = filtered.Where(x =>
                    x.FullName.Contains(search) ||
                    (x.Email != null && x.Email.Contains(search)) ||
                    (byPhone && x.Phone != null && x.Phone.Contains(digits)));
            }

            var role = NormaliseRole(query.Role);
            if (role is not null) filtered = filtered.Where(x => x.Role == role);

            if (string.Equals(query.Status, "active", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(x => x.IsActive);
            else if (string.Equals(query.Status, "inactive", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(x => !x.IsActive);

            // Admins first, then Staff, then Customers; A-Z inside each group.
            var ordered = filtered
                .OrderBy(x => x.Role == Roles.Admin ? 0 : x.Role == Roles.Staff ? 1 : 2)
                .ThenBy(x => x.FullName)
                .ThenBy(x => x.Id);

            var matches = await filtered.CountAsync();
            var pageCount = Math.Max(1, (int)Math.Ceiling(matches / (double)PageSize));
            var page = Math.Clamp(query.Page, 1, pageCount);

            // Just added someone: open the page they are on.
            if (!string.IsNullOrEmpty(query.FocusUserId))
            {
                var ids = await ordered.Select(x => x.Id).ToListAsync();
                var index = ids.IndexOf(query.FocusUserId);
                if (index >= 0) page = index / PageSize + 1;
            }

            var rows = await ordered.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();

            // "Inactive since 30 Oct": the latest Deactivated entry in the log.
            var inactiveIds = rows.Where(r => !r.IsActive).Select(r => r.Id).ToList();
            var since = inactiveIds.Count == 0
                ? new Dictionary<string, DateTime>()
                : await _db.UserAdminLogs.AsNoTracking()
                    .Where(l => inactiveIds.Contains(l.TargetUserId) && l.Action == UserAdminAction.Deactivated)
                    .GroupBy(l => l.TargetUserId)
                    .Select(g => new { g.Key, At = g.Max(l => l.DoneAt) })
                    .ToDictionaryAsync(x => x.Key, x => x.At);

            var list = rows.Select(r => new UserRow(
                r.Id, r.FullName, r.Email ?? "", r.Phone, r.Role, r.IsActive, r.CreatedAt,
                since.TryGetValue(r.Id, out var at) ? at : (DateTime?)null,
                r.HasPassword, r.Id == meId)).ToList();

            var log = await _db.UserAdminLogs.AsNoTracking()
                .OrderByDescending(l => l.DoneAt).ThenByDescending(l => l.Id)
                .Take(8)
                .Select(l => new UserLogRow(l.TargetName, l.Action, l.Detail, l.DoneByName, l.DoneAt))
                .ToListAsync();
            var logTotal = await _db.UserAdminLogs.CountAsync();

            return new UserListPage(list, matches, page, pageCount, PageSize, stats, log, logTotal, MaxActiveAdmins);
        }

        public async Task<UserLogPage> GetActivityAsync(string? search, UserAdminAction? action, int page)
        {
            var q = _db.UserAdminLogs.AsNoTracking().AsQueryable();

            var s = search?.Trim();
            if (!string.IsNullOrEmpty(s)) q = q.Where(l => l.TargetName.Contains(s) || l.DoneByName.Contains(s));
            if (action is not null) q = q.Where(l => l.Action == action);

            var matches = await q.CountAsync();
            var pageCount = Math.Max(1, (int)Math.Ceiling(matches / (double)LogPageSize));
            page = Math.Clamp(page, 1, pageCount);

            var rows = await q.OrderByDescending(l => l.DoneAt).ThenByDescending(l => l.Id)
                .Skip((page - 1) * LogPageSize).Take(LogPageSize)
                .Select(l => new UserLogRow(l.TargetName, l.Action, l.Detail, l.DoneByName, l.DoneAt))
                .ToListAsync();

            var counts = await _db.UserAdminLogs.AsNoTracking()
                .GroupBy(l => l.Action)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            return new UserLogPage(rows, matches, page, pageCount, LogPageSize, counts, counts.Values.Sum());
        }

        // ================= US05: add a user =================

        public async Task<UserAdminResult> CreateUserAsync(ClaimsPrincipal admin, NewUserInput input,
            Func<string, string, bool, string> buildPasswordLink)
        {
            var name = Regex.Replace(input.FullName?.Trim() ?? "", @"\s+", " ");
            if (name.Length == 0) return Bad("FullName", "Please enter their full name.");
            if (name.Length is < 2 or > 100) return Bad("FullName", "Full name must be 2 to 100 characters.");

            var email = input.Email?.Trim() ?? "";
            if (email.Length == 0) return Bad("Email", "Please enter their email.");
            if (email.Length > 100 || !new EmailAddressAttribute().IsValid(email))
                return Bad("Email", "Please enter a valid email address, e.g. name@example.com.");

            var role = NormaliseRole(input.Role);
            if (role is null) return Bad("Role", "Please choose a role.");

            // Phone is optional. Same rules as Register (libphonenumber).
            string? phone = null;
            if (!string.IsNullOrWhiteSpace(input.Phone))
            {
                var country = PhoneCountries.Find(input.CountryIso);
                if (country is null) return Bad("Phone", "Please choose a country code.");
                if (!Regex.IsMatch(input.Phone.Trim(), @"^[0-9+\-\s()]+$"))
                    return Bad("Phone", "Phone number can only contain digits.");
                phone = _accounts.FormatPhoneNumber(country.Iso, input.Phone.Trim());
                if (phone is null)
                    return Bad("Phone", $"That doesn't look like a valid {country.Name} ({country.DialCode}) phone number.");
            }

            if (await _users.FindByEmailAsync(email) is not null)
                return Bad("Email", "This email already has an account. Search for it in the list instead.");

            var me = await MeAsync(admin);

            // A new Admin: limit + the Admin proves it's really them.
            if (role == Roles.Admin)
            {
                var blocked = await CheckNewAdminAsync(me.User, input.MyPassword, exceptUserId: null);
                if (blocked is not null) return blocked;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,      // the set-password link proves they own the inbox
                FullName = name,
                PhoneNumber = phone,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            // No password: the new user chooses one from the email.
            var created = await _users.CreateAsync(user);
            if (!created.Succeeded)
            {
                if (created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
                    return Bad("Email", "This email already has an account. Search for it in the list instead.");
                return new(false, string.Join(" ", created.Errors.Select(e => e.Description)));
            }

            var added = await _users.AddToRoleAsync(user, role);
            if (!added.Succeeded)
            {
                // Brand-new and unused, so it is safe to take it away again rather than leave an account with no role.
                await _users.DeleteAsync(user);
                return new(false, "The account couldn't be created. Please try again.");
            }

            await LogAsync(user, UserAdminAction.Created, $"as {role}", me);
            await SendLinkEmailAsync(user, role, welcome: true, buildPasswordLink);
            if (role == Roles.Admin) await AlertAdminsAsync(user, me.Name, "was added as a new Admin");

            _logger.LogInformation("User {UserId} created as {Role} by {AdminId}.", user.Id, role, me.Id);
            return new(true, $"{name} was added as {role}. We've emailed them a link to set their password.", UserId: user.Id);
        }

        // ================= US05: change role =================

        public async Task<UserAdminResult> ChangeRoleAsync(ClaimsPrincipal admin, string? userId, string? role, string? myPassword)
        {
            var newRole = NormaliseRole(role);
            if (newRole is null) return new(false, "Please choose a role.", "Role");

            var user = await FindAsync(userId);
            if (user is null) return new(false, "That user no longer exists.");

            var me = await MeAsync(admin);
            if (user.Id == me.Id) return new(false, "You can't change your own role. Ask another Admin.");

            var current = await RoleOfAsync(user);
            if (current == newRole) return new(true, $"{user.FullName} is already {newRole}.");

            if (current == Roles.Admin && user.IsActive && await ActiveAdminCountAsync(except: user.Id) == 0)
                return new(false, "There must always be at least one active Admin.");

            if (newRole == Roles.Admin)
            {
                var blocked = await CheckNewAdminAsync(me.User, myPassword, exceptUserId: user.Id, countsIfActive: user.IsActive);
                if (blocked is not null) return blocked;
            }

            var oldRoles = await _users.GetRolesAsync(user);
            if (oldRoles.Count > 0)
            {
                var removed = await _users.RemoveFromRolesAsync(user, oldRoles);
                if (!removed.Succeeded) return new(false, "The role couldn't be changed. Please try again.");
            }

            var added = await _users.AddToRoleAsync(user, newRole);
            if (!added.Succeeded)
            {
                await _users.AddToRolesAsync(user, oldRoles);   // put the old role back
                return new(false, "The role couldn't be changed. Please try again.");
            }

            // No logout needed: their login is re-checked every minute (Program.cs) and picks up the new role.
            await LogAsync(user, UserAdminAction.RoleChanged, $"{current} → {newRole}", me);
            if (newRole == Roles.Admin) await AlertAdminsAsync(user, me.Name, $"was changed from {current} to Admin");

            _logger.LogInformation("User {UserId} role {Old} -> {New} by {AdminId}.", user.Id, current, newRole, me.Id);
            return new(true, $"{user.FullName} is now {newRole}. Their pages change within a minute.");
        }

        // ================= US07: deactivate / reactivate =================

        public async Task<UserAdminResult> SetActiveAsync(ClaimsPrincipal admin, string? userId, bool active)
        {
            var user = await FindAsync(userId);
            if (user is null) return new(false, "That user no longer exists.");

            var me = await MeAsync(admin);
            if (user.Id == me.Id) return new(false, "You can't deactivate your own account.");

            if (user.IsActive == active)
                return new(true, $"{user.FullName} is already {(active ? "active" : "inactive")}.");

            var isAdmin = await RoleOfAsync(user) == Roles.Admin;
            if (!active && isAdmin && await ActiveAdminCountAsync(except: user.Id) == 0)
                return new(false, "There must always be at least one active Admin.");
            if (active && isAdmin && await ActiveAdminCountAsync(except: user.Id) >= MaxActiveAdmins)
                return new(false, $"There can be at most {MaxActiveAdmins} active Admins. Deactivate or change another Admin first.");

            user.IsActive = active;
            var saved = await _users.UpdateAsync(user);
            if (!saved.Succeeded) return new(false, "The account couldn't be updated. Please try again.");

            if (!active)
            {
                // A new security stamp makes every existing login invalid: they are logged out within a minute,
                // and Login refuses inactive accounts (AccountService.LoginAsync). Old reset links stop working too.
                await _users.UpdateSecurityStampAsync(user);
            }

            await LogAsync(user, active ? UserAdminAction.Reactivated : UserAdminAction.Deactivated, null, me);

            _logger.LogInformation("User {UserId} {State} by {AdminId}.", user.Id, active ? "reactivated" : "deactivated", me.Id);
            return new UserAdminResult(true, active
                ? $"{user.FullName} can log in again."
                : $"{user.FullName} was deactivated and will be logged out within a minute.");
        }

        // ================= US07: send a password link =================

        public async Task<UserAdminResult> SendPasswordLinkAsync(ClaimsPrincipal admin, string? userId,
            Func<string, string, bool, string> buildPasswordLink)
        {
            var user = await FindAsync(userId);
            if (user is null) return new(false, "That user no longer exists.");
            if (!user.IsActive) return new(false, $"{user.FullName} is inactive. Reactivate the account first.");
            if (string.IsNullOrEmpty(user.Email)) return new(false, $"{user.FullName} has no email address.");

            // Never set a password (new account) -> the welcome "Set your password" email again.
            var welcome = !await _users.HasPasswordAsync(user);
            var role = await RoleOfAsync(user);

            await SendLinkEmailAsync(user, role, welcome, buildPasswordLink);

            var me = await MeAsync(admin);
            await LogAsync(user, UserAdminAction.PasswordLinkSent, welcome ? "set-password link" : "reset link", me);

            return new(true, $"We've emailed {EmailTemplates.Mask(user.Email)} a link to {(welcome ? "set" : "reset")} their password (works once, for 1 hour).");
        }

        // ================= US07: remove a Staff / Admin account =================

        public async Task<UserAdminResult> RemoveUserAsync(ClaimsPrincipal admin, string? userId)
        {
            var user = await FindAsync(userId);
            if (user is null) return new(false, "That user no longer exists.");

            var me = await MeAsync(admin);
            if (user.Id == me.Id) return new(false, "You can't remove your own account.");

            var role = await RoleOfAsync(user);
            if (role == Roles.Customer)
                return new(false, "Customer accounts can't be removed: their reservations, orders and bills belong to them. Deactivate it instead.");
            if (user.IsActive)
                return new(false, $"Deactivate {user.FullName} first, then remove the account.");

            var name = user.FullName;

            // Logged first, while the account still has its details.
            await LogAsync(user, UserAdminAction.Removed, $"was {role}", me);

            // Keep the row (bills, payments and logs point to it) but take away everything that identifies or opens it.
            var placeholder = $"removed-{user.Id}@removed.invalid";
            await _users.SetEmailAsync(user, placeholder);
            await _users.SetUserNameAsync(user, placeholder);
            await _users.SetPhoneNumberAsync(user, null);
            if (await _users.HasPasswordAsync(user)) await _users.RemovePasswordAsync(user);

            user.IsActive = false;
            user.RemovedAt = DateTime.Now;
            user.DiningPreferences = null;
            await _users.UpdateAsync(user);
            await _users.UpdateSecurityStampAsync(user);

            _logger.LogInformation("User {UserId} removed by {AdminId}.", user.Id, me.Id);
            return new(true, $"{name}'s account was removed. Their name stays in old bills and logs only.");
        }

        // ================= helpers =================

        private static UserAdminResult Bad(string field, string message) => new(false, message, field);

        private static string? NormaliseRole(string? role) =>
            AllRoles.FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase));

        // Removed accounts can't be changed any more.
        private async Task<ApplicationUser?> FindAsync(string? userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;
            var user = await _users.FindByIdAsync(userId);
            return user is null || user.RemovedAt is not null ? null : user;
        }

        private async Task<string> RoleOfAsync(ApplicationUser user) =>
            (await _users.GetRolesAsync(user)).FirstOrDefault() ?? Roles.Customer;

        private Task<int> ActiveAdminCountAsync(string? except) =>
            (from ur in _db.UserRoles
             join r in _db.Roles on ur.RoleId equals r.Id
             join u in _db.Users on ur.UserId equals u.Id
             where r.Name == Roles.Admin && u.IsActive && u.RemovedAt == null && (except == null || u.Id != except)
             select u.Id).Distinct().CountAsync();

        // New Admin (created, promoted): the limit, then the Admin's own password.
        // countsIfActive: the promoted user is active, so they would add one more active Admin.
        private async Task<UserAdminResult?> CheckNewAdminAsync(ApplicationUser? me, string? myPassword,
            string? exceptUserId, bool countsIfActive = true)
        {
            if (countsIfActive && await ActiveAdminCountAsync(exceptUserId) >= MaxActiveAdmins)
                return Bad("Role", $"There can be at most {MaxActiveAdmins} active Admins. Deactivate or change another Admin first.");

            if (me is null) return new(false, "Please log in again.");

            if (await _users.IsLockedOutAsync(me))
                return Bad("MyPassword", "Too many wrong passwords. Please try again in 15 minutes.");

            if (string.IsNullOrEmpty(myPassword))
                return Bad("MyPassword", "Type your own password to add an Admin.");

            if (!await _users.CheckPasswordAsync(me, myPassword))
            {
                // Counts like a wrong login (5 tries -> 15 minutes), so nobody can guess it from an open laptop.
                await _users.AccessFailedAsync(me);
                return Bad("MyPassword", "That's not your password.");
            }

            await _users.ResetAccessFailedCountAsync(me);
            return null;
        }

        private async Task<(string? Id, string Name, ApplicationUser? User)> MeAsync(ClaimsPrincipal admin)
        {
            var me = await _users.GetUserAsync(admin);
            var name = me is null || string.IsNullOrWhiteSpace(me.FullName) ? "Admin" : me.FullName;
            return (me?.Id, name, me);
        }

        private async Task LogAsync(ApplicationUser user, UserAdminAction action, string? detail,
            (string? Id, string Name, ApplicationUser? User) me)
        {
            _db.UserAdminLogs.Add(new UserAdminLog
            {
                TargetUserId = user.Id,
                TargetName = user.FullName,
                Action = action,
                Detail = detail,
                DoneByUserId = me.Id,
                DoneByName = me.Name,
                DoneAt = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }

        // Security alert to every active Admin (including the one who did it) when a new Admin appears.
        private async Task AlertAdminsAsync(ApplicationUser newAdmin, string doneBy, string what)
        {
            var admins = await (from ur in _db.UserRoles
                                join r in _db.Roles on ur.RoleId equals r.Id
                                join u in _db.Users on ur.UserId equals u.Id
                                where r.Name == Roles.Admin && u.IsActive && u.RemovedAt == null && u.Id != newAdmin.Id && u.Email != null
                                select new { u.Email, u.FullName }).Distinct().ToListAsync();

            var footer = await FooterAsync();
            var when = DateTime.Now.ToString("ddd, d MMM yyyy · h:mm tt", CultureInfo.InvariantCulture);

            foreach (var a in admins)
            {
                var html = EmailTemplates.Build(
                    kicker: "Account security",
                    title: "A new Admin was added",
                    name: a.FullName,
                    introHtml: $"<b>{EmailTemplates.Encode(newAdmin.FullName)}</b> {EmailTemplates.Encode(what)} on the YORU system. " +
                               "Admins can change every setting and every account.",
                    rows: new[]
                    {
                        new EmailTemplates.Row("New Admin", $"{newAdmin.FullName} ({newAdmin.Email})"),
                        new EmailTemplates.Row("Done by", doneBy),
                        new EmailTemplates.Row("When", when)
                    },
                    warnHtml: "<b>Not expected?</b> Log in, deactivate that account on Admin → Users, and change your password.",
                    smallText: "Every Admin gets this email whenever a new Admin appears.",
                    footerLine: footer);

                try { await _email.SendEmailAsync(a.Email!, "Security alert: a new YORU Admin was added", html); }
                catch (Exception ex) { _logger.LogError(ex, "Admin alert email failed."); }
            }
        }

        // One-time link (PasswordReset token provider: 1 hour), same page as US52.
        private async Task SendLinkEmailAsync(ApplicationUser user, string role, bool welcome,
            Func<string, string, bool, string> buildPasswordLink)
        {
            if (string.IsNullOrEmpty(user.Email)) return;

            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = buildPasswordLink(user.Id, code, welcome);
            var footer = await FooterAsync();

            var html = welcome
                ? EmailTemplates.Build(
                    kicker: "Welcome to YORU",
                    title: "Set your password",
                    name: user.FullName,
                    introHtml: $"An account has been created for you at {EmailTemplates.Encode(Brand.FullName)} " +
                               $"as <b>{EmailTemplates.Encode(role)}</b>. Choose your password to start using it.",
                    rows: new[]
                    {
                        new EmailTemplates.Row("Log in with", user.Email),
                        new EmailTemplates.Row("Role", role),
                        new EmailTemplates.Row("Link works for", "1 hour, once")
                    },
                    buttonText: "Set my password",
                    buttonUrl: link,
                    smallText: "Link expired? Ask the restaurant to send you a new one.",
                    footerLine: footer)
                : EmailTemplates.Build(
                    kicker: "Password reset",
                    title: "Choose a new password",
                    name: user.FullName,
                    introHtml: "The YORU team sent you this link so you can choose a new password for your account.",
                    rows: new[]
                    {
                        new EmailTemplates.Row("Account", user.Email),
                        new EmailTemplates.Row("Link works for", "1 hour, once")
                    },
                    buttonText: "Choose a new password",
                    buttonUrl: link,
                    smallText: "Didn't expect this? Ignore this email and your password stays the same.",
                    footerLine: footer);

            var subject = welcome ? $"Set your password for {Brand.FullName}" : "Reset your YORU password";
            try { await _email.SendEmailAsync(user.Email, subject, html); }
            catch (Exception ex) { _logger.LogError(ex, "Password link email to user {UserId} failed.", user.Id); }
        }

        private async Task<string> FooterAsync()
        {
            try
            {
                var info = await _home.GetInfoAsync();
                var address = info.Address.Replace("\r", "").Replace("\n", ", ");
                return $"{Brand.FullName} · {address} · {info.Phone}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read restaurant details for the email footer.");
                return Brand.FullName;
            }
        }
    }
}
