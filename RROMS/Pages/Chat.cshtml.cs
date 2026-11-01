using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages
{
    // US08 (Owner: Member 1): the chat bubble on the customer pages talks to this page (JSON only).
    //   POST /chat?handler=Ask    { question, history: [{ role, text }] }  -> reply
    //   GET  /chat?handler=Faqs   -> shown FAQs (US10)
    //   GET  /chat?handler=Hours  -> opening-hours card
    // POSTs carry the anti-forgery token (header "RequestVerificationToken"), like every form in the site.
    [AllowAnonymous]
    public class ChatModel : PageModel
    {
        private readonly IChatbotService _chat;

        public ChatModel(IChatbotService chat)
        {
            _chat = chat;
        }

        public class AskRequest
        {
            public string? Question { get; set; }
            public List<ChatTurn>? History { get; set; }
        }

        // Not a page of its own.
        public IActionResult OnGet() => RedirectToPage("/Index");

        public async Task<IActionResult> OnPostAskAsync([FromBody] AskRequest? request)
        {
            // The question limit is per visitor (IP address).
            var visitor = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var reply = await _chat.AskAsync(request?.Question, request?.History, visitor);
            return new JsonResult(reply);
        }

        public async Task<IActionResult> OnGetFaqsAsync() => new JsonResult(await _chat.GetFaqsAsync());

        public async Task<IActionResult> OnGetHoursAsync() => new JsonResult(await _chat.GetHoursAsync());
    }
}
