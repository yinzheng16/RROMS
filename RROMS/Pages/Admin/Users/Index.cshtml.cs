using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin.Users
{
    // US05 / US07 (Owner: Member 1): Admin → Users.
    // Admins only (the whole /Admin folder uses the "AdminOnly" policy in Program.cs).
    // All rules live in UserAdminService; this page only reads the form and shows the result.
    public class IndexModel : PageModel
    {
        private readonly IUserAdminService _admin;

        public IndexModel(IUserAdminService admin)
        {
            _admin = admin;
        }

        // ---------- filters (in the address bar, and sent back with every button so the list stays the same) ----------
        [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
        [BindProperty(SupportsGet = true, Name = "role")] public string? RoleFilter { get; set; }
        [BindProperty(SupportsGet = true, Name = "status")] public string? StatusFilter { get; set; }
        [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;

        // ---------- "Add user" pop-up ----------
        [BindProperty] public NewUserForm NewUser { get; set; } = new();

        public class NewUserForm
        {
            public string? FullName { get; set; }
            public string? Email { get; set; }
            public string CountryIso { get; set; } = "MY";
            public string? Phone { get; set; }
            public string Role { get; set; } = Roles.Staff;
            public string? MyPassword { get; set; }   // only when adding an Admin
        }

        public UserListPage Data { get; private set; } = null!;
        public bool OpenAddUser { get; private set; }
        public string? HighlightId { get; private set; }   // the account just added (glows for a moment)
        public IReadOnlyList<PhoneCountries.Country> Countries => PhoneCountries.All;

        public async Task OnGetAsync()
        {
            HighlightId = TempData["NewUserId"] as string;
            await LoadAsync(HighlightId);
        }

        public async Task<IActionResult> OnPostCreateAsync()
        {
            var result = await _admin.CreateUserAsync(User,
                new NewUserInput(NewUser.FullName, NewUser.Email, NewUser.CountryIso, NewUser.Phone, NewUser.Role, NewUser.MyPassword),
                PasswordLink);

            if (!result.Ok)
            {
                ModelState.AddModelError(result.Field is null ? string.Empty : $"NewUser.{result.Field}", result.Message);
                OpenAddUser = true;   // show the pop-up again with what was typed
                NewUser.MyPassword = null;   // never send a password back to the page
                await LoadAsync();
                return Page();
            }

            // Back to the full list, on the page with the new account, which glows for a moment.
            TempData["SuccessMessage"] = result.Message;
            TempData["NewUserId"] = result.UserId;
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRoleAsync(string? userId, string? newRole, string? myPassword) =>
            Done(await _admin.ChangeRoleAsync(User, userId, newRole, myPassword));

        public async Task<IActionResult> OnPostDeactivateAsync(string? userId) =>
            Done(await _admin.SetActiveAsync(User, userId, active: false));

        public async Task<IActionResult> OnPostReactivateAsync(string? userId) =>
            Done(await _admin.SetActiveAsync(User, userId, active: true));

        public async Task<IActionResult> OnPostSendLinkAsync(string? userId) =>
            Done(await _admin.SendPasswordLinkAsync(User, userId, PasswordLink));

        public async Task<IActionResult> OnPostRemoveAsync(string? userId) =>
            Done(await _admin.RemoveUserAsync(User, userId));

        // ---------- helpers ----------

        private async Task LoadAsync(string? focusUserId = null)
        {
            Data = await _admin.GetUsersAsync(User, new UserListQuery(Search, RoleFilter, StatusFilter, PageNo, focusUserId));
            PageNo = Data.Page;
        }

        // Toast, then back to the same search / filter / page.
        private IActionResult Done(UserAdminResult result)
        {
            TempData[result.Ok ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return RedirectToPage(Filters());
        }

        // Same page as US52 Forgot password. welcome=true -> "Set your password" wording.
        private string PasswordLink(string userId, string code, bool welcome) =>
            Url.Page("/Account/ResetPassword", pageHandler: null,
                values: new { area = "Identity", userId, code, welcome = welcome ? true : (bool?)null },
                protocol: Request.Scheme)
            ?? $"{Request.Scheme}://{Request.Host}/Identity/Account/ResetPassword?userId={Uri.EscapeDataString(userId)}&code={Uri.EscapeDataString(code)}{(welcome ? "&welcome=true" : "")}";

        // Route values for the list as it is now (empty ones are left out of the address).
        public object Filters(int? page = null)
        {
            var p = page ?? PageNo;
            return new
            {
                q = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                role = string.IsNullOrEmpty(RoleFilter) ? null : RoleFilter,
                status = string.IsNullOrEmpty(StatusFilter) ? null : StatusFilter,
                p = p > 1 ? p : (int?)null
            };
        }

        public string? ErrorFor(string field) =>
            ModelState[$"NewUser.{field}"]?.Errors.FirstOrDefault()?.ErrorMessage;
    }
}
