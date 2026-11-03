using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using RROMS.Models;
using RROMS.Services;

namespace RROMS.Pages.Dev
{
    // EN07 (Member 1): developer page for the shared email sender. Development only (404 otherwise).
    // Shows the last emails the app tried to send, with their links, and can send a test email.
    public class EmailsModel : PageModel
    {
        private readonly IEmailSender _email;
        private readonly EmailOutbox _outbox;
        private readonly EmailSettings _settings;
        private readonly IWebHostEnvironment _env;
        private readonly UserManager<ApplicationUser> _users;

        public EmailsModel(IEmailSender email, EmailOutbox outbox, IOptions<EmailSettings> settings,
                           IWebHostEnvironment env, UserManager<ApplicationUser> users)
        {
            _email = email;
            _outbox = outbox;
            _settings = settings.Value;
            _env = env;
            _users = users;
        }

        public IReadOnlyList<OutboxEmail> Emails { get; private set; } = [];
        public bool CanSend => _settings.CanSend;
        public string FromAddress => _settings.FromAddress;

        [BindProperty, Required(ErrorMessage = "Enter an email address."),
         EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string To { get; set; } = "";

        [TempData] public string? Message { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!_env.IsDevelopment()) return NotFound();

            // Default "To": the signed-in user's email, otherwise the restaurant's own address.
            var me = User.Identity?.IsAuthenticated == true ? await _users.GetUserAsync(User) : null;
            To = me?.Email ?? _settings.FromAddress;
            Emails = _outbox.All();
            return Page();
        }

        public async Task<IActionResult> OnPostTestAsync()
        {
            if (!_env.IsDevelopment()) return NotFound();
            if (!ModelState.IsValid)
            {
                Emails = _outbox.All();
                return Page();
            }

            var home = $"{Request.Scheme}://{Request.Host}/";
            var html = $"""
                <div style="font-family:Georgia,serif;background:#F5F0E7;padding:24px;color:#191919">
                  <h2 style="margin:0 0 8px">{WebUtility.HtmlEncode(Brand.FullName)}</h2>
                  <p>This is a test email from the shared email sender (EN07).</p>
                  <p>If you can read this in your inbox, Gmail sending works.</p>
                  <p><a href="{home}" style="color:#B23526">Open the YORU website</a></p>
                  <p style="color:#5C5F55;font-size:12px">Sent {DateTime.Now:d MMM yyyy, h:mm tt}</p>
                </div>
                """;

            await _email.SendEmailAsync(To.Trim(), $"{Brand.FullName} · Test email", html);

            var last = _outbox.All().FirstOrDefault();
            Message = last == null ? "Done." : last.Status;
            return RedirectToPage();
        }

        public IActionResult OnPostClear()
        {
            if (!_env.IsDevelopment()) return NotFound();
            _outbox.Clear();
            Message = "List cleared.";
            return RedirectToPage();
        }
    }
}
