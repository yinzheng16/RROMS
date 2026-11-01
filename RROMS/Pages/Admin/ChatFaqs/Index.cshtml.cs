using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin.ChatFaqs
{
    // US10 (Owner: Member 1): Admin → Restaurant → Chatbot FAQs.
    // Admins only (the /Admin folder uses the "AdminOnly" policy). Rules live in ChatFaqService.
    public class IndexModel : PageModel
    {
        private readonly IChatFaqService _faqs;
        private readonly IChatbotService _chat;

        public IndexModel(IChatFaqService faqs, IChatbotService chat)
        {
            _faqs = faqs;
            _chat = chat;
        }

        // ---------- filters (kept in the address bar and sent back with every button) ----------
        [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
        [BindProperty(SupportsGet = true, Name = "status")] public string? StatusFilter { get; set; }
        [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;

        // ---------- Add / Edit pop-up ----------
        [BindProperty] public FaqForm Faq { get; set; } = new();

        public class FaqForm
        {
            public int Id { get; set; }
            public string? Question { get; set; }
            public string? Answer { get; set; }
            public string? Keywords { get; set; }
            public bool IsActive { get; set; } = true;
        }

        public FaqListPage Data { get; private set; } = null!;
        public bool AiOn => _chat.AiConfigured;
        public bool OpenEditor { get; private set; }
        public int? HighlightId { get; private set; }

        public async Task OnGetAsync()
        {
            HighlightId = TempData["FaqId"] as int?;
            await LoadAsync();
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            var isNew = Faq.Id == 0;
            FaqResult result;
            try
            {
                result = await _faqs.SaveAsync(new FaqInput(Faq.Id, Faq.Question, Faq.Answer, Faq.Keywords, Faq.IsActive));
            }
            catch (KeyNotFoundException)
            {
                TempData["ErrorMessage"] = "That FAQ no longer exists.";
                return RedirectToPage(Filters());
            }

            if (!result.Ok)
            {
                ModelState.AddModelError(result.Field is null ? string.Empty : $"Faq.{result.Field}", result.Message);
                OpenEditor = true;   // show the pop-up again with what was typed
                await LoadAsync();
                return Page();
            }

            TempData["SuccessMessage"] = result.Message;
            TempData["FaqId"] = result.Id;
            if (!isNew) return RedirectToPage(Filters());

            // A new FAQ is the last one: open the last page, without filters, so it can be seen.
            var last = (await _faqs.GetFaqsAsync(new FaqListQuery(null, null, 1))).PageCount;
            return RedirectToPage(new { p = last > 1 ? last : (int?)null });
        }

        public async Task<IActionResult> OnPostToggleAsync(int id, bool shown) =>
            Done(await _faqs.SetShownAsync(id, shown));

        public async Task<IActionResult> OnPostDeleteAsync(int id) =>
            Done(await _faqs.DeleteAsync(id));

        // "Try the assistant": the same answer a customer would get (JSON, called by admin-chatfaqs.js).
        public class TryRequest
        {
            public string? Question { get; set; }
        }

        public async Task<IActionResult> OnPostTryAsync([FromBody] TryRequest? request)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var reply = await _chat.TryAsync(request?.Question);
            watch.Stop();
            var faqs = await _chat.GetFaqsAsync();
            var used = reply.FaqId is int id ? faqs.FirstOrDefault(f => f.Id == id) : null;
            return new JsonResult(new
            {
                reply.Text,
                reply.Source,
                Faq = used is null ? null : new { used.Id, used.Question },
                Hours = reply.Hours is not null,
                Ms = watch.ElapsedMilliseconds   // how long the answer took (shown as "1.8 s")
            });
        }

        // ---------- helpers ----------

        private async Task LoadAsync()
        {
            Data = await _faqs.GetFaqsAsync(new FaqListQuery(Search, StatusFilter, PageNo));
            PageNo = Data.Page;
        }

        private IActionResult Done(FaqResult result)
        {
            TempData[result.Ok ? "SuccessMessage" : "ErrorMessage"] = result.Message;
            return RedirectToPage(Filters());
        }

        // Route values for the list as it is now (empty ones are left out of the address).
        public object Filters(int? page = null)
        {
            var p = page ?? PageNo;
            return new
            {
                q = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                status = string.IsNullOrEmpty(StatusFilter) ? null : StatusFilter,
                p = p > 1 ? p : (int?)null
            };
        }

        public string? ErrorFor(string field) =>
            ModelState[$"Faq.{field}"]?.Errors.FirstOrDefault()?.ErrorMessage;
    }
}
