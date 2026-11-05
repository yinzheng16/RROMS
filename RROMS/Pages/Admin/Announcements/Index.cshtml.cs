using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin.Announcements
{
    // US39 (Owner: Member 4): the admin writes announcements / campaigns, sends them to customers
    // (in-app + email) and controls the home page banner. All rules live in IAnnouncementService.
    // Design: wwwroot/css/admin-announcements.css (an- classes), script: wwwroot/js/admin-announcements.js.
    [Authorize(Policy = "AdminOnly")]
    public class IndexModel : PageModel
    {
        private readonly IAnnouncementService _announcements;

        public IndexModel(IAnnouncementService announcements) => _announcements = announcements;

        // List filters (GET): search text and status chip ("live", "scheduled", "stopped", "ended").
        [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
        [BindProperty(SupportsGet = true, Name = "status")] public string? Status { get; set; }

        // Without JavaScript: ?New=true opens an empty form, ?Edit=5 opens announcement 5,
        // ?Copy=5 opens a new form filled from announcement 5.
        [BindProperty(SupportsGet = true)] public bool New { get; set; }
        [BindProperty(SupportsGet = true)] public int? Edit { get; set; }
        [BindProperty(SupportsGet = true)] public int? Copy { get; set; }

        [BindProperty] public AnnouncementInput Input { get; set; } = new();

        public List<AnnouncementRow> All { get; private set; } = new();
        public List<AnnouncementRow> Rows { get; private set; } = new();
        // Live announcements taking turns on the home page (newest first, up to AnnouncementBanner.MaxSlides).
        public List<AnnouncementBannerItem> OnHomePage { get; private set; } = new();
        public int RecipientCount { get; private set; }

        // The form pop-up opens when the page loads (no-JS links, or the server found a problem).
        public bool OpenForm { get; private set; }
        public string? FormError { get; private set; }
        public string? FormErrorField { get; private set; }

        public AnnouncementState? StatusFilter => Status?.ToLowerInvariant() switch
        {
            "live" => AnnouncementState.Running,
            "scheduled" => AnnouncementState.Scheduled,
            "stopped" => AnnouncementState.Stopped,
            "ended" => AnnouncementState.Ended,
            _ => null
        };

        public string? SearchText => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

        public int Count(AnnouncementState state) => All.Count(r => r.State == state);

        public async Task<IActionResult> OnGetAsync()
        {
            if (Edit is int id)
            {
                var input = await _announcements.GetInputAsync(id);
                if (input is null)
                {
                    TempData["ErrorMessage"] = "This announcement no longer exists.";
                    return RedirectToPage();
                }
                Input = input;
                OpenForm = true;
            }
            else if (Copy is int copyId)
            {
                var copy = await _announcements.GetCopyAsync(copyId);
                if (copy is null)
                {
                    TempData["ErrorMessage"] = "This announcement no longer exists.";
                    return RedirectToPage();
                }
                Input = copy;
                OpenForm = true;
            }
            else if (New)
            {
                OpenForm = true;
            }

            await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (adminId is null) return Challenge();

            var result = await _announcements.SaveAsync(Input, adminId);
            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;   // team toast (shared layout)
                return RedirectToPage(new { q = SearchText, status = Status });
            }

            // Keep what the admin typed and show the problem in the pop-up.
            FormError = result.Message;
            FormErrorField = result.Field;
            OpenForm = true;
            if (Input.Id != 0) Input.IsSent = (await _announcements.GetInputAsync(Input.Id))?.IsSent ?? false;
            await LoadAsync();
            return Page();
        }

        public Task<IActionResult> OnPostSendAsync(int id) => RunAsync(_announcements.SendAsync(id));
        public Task<IActionResult> OnPostStopAsync(int id) => RunAsync(_announcements.StopAsync(id));
        public Task<IActionResult> OnPostResumeAsync(int id) => RunAsync(_announcements.ResumeAsync(id));
        public Task<IActionResult> OnPostDeleteAsync(int id) => RunAsync(_announcements.DeleteAsync(id));

        private async Task<IActionResult> RunAsync(Task<AnnouncementResult> action)
        {
            var result = await action;
            TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return RedirectToPage(new { q = SearchText, status = Status });
        }

        // "Banner button goes to" choices (the select picks Input.Link).
        public IEnumerable<SelectListItem> LinkOptions =>
            Enum.GetValues<AnnouncementLink>().Select(l => new SelectListItem(AnnouncementLinks.Option(l), l.ToString()));

        public string? FieldError(string field) => FormErrorField == field ? FormError : null;

        // Form error that belongs to no field (e.g. "no longer exists").
        public string? GeneralError => FormError is not null && FormErrorField is null ? FormError : null;

        public string ListUrl(string? status = null, bool keepStatus = true, bool keepSearch = true) =>
            Url.Page("./Index", new
            {
                q = keepSearch ? SearchText : null,
                status = keepStatus ? Status : status
            })!;

        private async Task LoadAsync()
        {
            All = await _announcements.GetAllAsync();
            var rows = All.AsEnumerable();
            if (StatusFilter is AnnouncementState state) rows = rows.Where(r => r.State == state);
            if (SearchText is string term)
                rows = rows.Where(r => r.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                                    || r.Message.Contains(term, StringComparison.OrdinalIgnoreCase));
            Rows = rows.ToList();
            OnHomePage = await _announcements.GetRunningAsync();
            RecipientCount = await _announcements.CountRecipientsAsync();
        }
    }
}
