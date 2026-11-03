using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin.Users
{
    // US07 (Owner: Member 1): Admin → Users → Activity. The full, dated history of every account change.
    public class ActivityModel : PageModel
    {
        private readonly IUserAdminService _admin;

        public ActivityModel(IUserAdminService admin)
        {
            _admin = admin;
        }

        [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
        [BindProperty(SupportsGet = true, Name = "type")] public string? ActionFilter { get; set; }
        [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;

        public UserLogPage Data { get; private set; } = null!;

        // Filter value (the action's name), label, Bootstrap icon, colour class.
        public static readonly (string Value, string Label, string Icon, string Tone)[] Types =
        {
            (nameof(UserAdminAction.Created), "Created", "bi-person-plus", "is-add"),
            (nameof(UserAdminAction.RoleChanged), "Role changed", "bi-arrow-left-right", "is-role"),
            (nameof(UserAdminAction.Deactivated), "Deactivated", "bi-person-x", "is-off"),
            (nameof(UserAdminAction.Reactivated), "Reactivated", "bi-person-check", "is-on"),
            (nameof(UserAdminAction.PasswordLinkSent), "Password links", "bi-key", "is-link"),
            (nameof(UserAdminAction.Removed), "Removed", "bi-trash3", "is-gone")
        };

        public async Task OnGetAsync()
        {
            UserAdminAction? action = Enum.TryParse<UserAdminAction>(ActionFilter, ignoreCase: true, out var a) ? a : null;
            Data = await _admin.GetActivityAsync(Search, action, PageNo);
            PageNo = Data.Page;
        }

        public object Filters(int? page = null, string? type = null, bool keepType = true)
        {
            var p = page ?? 1;
            return new
            {
                q = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                type = keepType ? (string.IsNullOrEmpty(ActionFilter) ? null : ActionFilter) : (string.IsNullOrEmpty(type) ? null : type),
                p = p > 1 ? p : (int?)null
            };
        }
    }
}
