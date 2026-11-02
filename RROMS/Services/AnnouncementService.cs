using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // US39 (Member 4): announcements and promotional campaigns.
    //   - The admin writes a title, a message and a date range.
    //   - Home page: a banner while the dates include today (and it is not stopped).
    //   - Send: once, to every active customer:
    //       in-app (US37) through INotificationService (Promotion type: no automatic email),
    //       email in the announcement design (AnnouncementEmail) through the shared IEmailSender (EN07).
    //     One customer failing never stops the others: every failure is logged only.
    public class AnnouncementService : IAnnouncementService
    {
        private readonly IRepository<Announcement> _announcements;
        private readonly UserManager<ApplicationUser> _users;
        private readonly INotificationService _notifications;
        private readonly IEmailSender _email;
        private readonly IHomeService _home;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<AnnouncementService> _logger;

        public AnnouncementService(
            IRepository<Announcement> announcements,
            UserManager<ApplicationUser> users,
            INotificationService notifications,
            IEmailSender email,
            IHomeService home,
            IHttpContextAccessor http,
            ILogger<AnnouncementService> logger)
        {
            _announcements = announcements;
            _users = users;
            _notifications = notifications;
            _email = email;
            _home = home;
            _http = http;
            _logger = logger;
        }

        // ======================= Admin list / form =======================

        public async Task<List<AnnouncementRow>> GetAllAsync()
        {
            var now = DateTime.Now;
            var list = await _announcements.Query()
                .AsNoTracking()
                .OrderByDescending(a => a.CreatedAt)
                .ThenByDescending(a => a.Id)
                .ToListAsync();

            return list.Select(a => new AnnouncementRow(
                a.Id, a.Title, a.Message, a.StartDate, a.EndDate, StateOf(a, now),
                a.CreatedAt, a.SentAt, a.RecipientCount, a.Link)).ToList();
        }

        public async Task<AnnouncementInput?> GetInputAsync(int id)
        {
            var a = await _announcements.Query().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            return a is null ? null : new AnnouncementInput
            {
                Id = a.Id,
                Title = a.Title,
                Message = a.Message,
                StartDate = a.StartDate,
                EndDate = a.EndDate,
                Link = a.Link,
                IsSent = a.SentAt.HasValue
            };
        }

        public async Task<AnnouncementInput?> GetCopyAsync(int id)
        {
            var copy = await GetInputAsync(id);
            if (copy is null) return null;

            // Same text and button, new and unsent; a start that passed moves to now (same length).
            var length = copy.EndDate - copy.StartDate;
            var now = AnnouncementTimes.Minute(DateTime.Now);
            if (copy.StartDate < now)
            {
                copy.StartDate = now;
                copy.EndDate = now + length;
            }
            copy.Id = 0;
            copy.IsSent = false;
            return copy;
        }

        public async Task<AnnouncementResult> SaveAsync(AnnouncementInput input, string adminUserId)
        {
            var title = (input.Title ?? "").Trim();
            var message = (input.Message ?? "").Trim();
            var start = AnnouncementTimes.Minute(input.StartDate);
            var end = AnnouncementTimes.Minute(input.EndDate);
            var link = Enum.IsDefined(input.Link) ? input.Link : AnnouncementLink.None;

            if (end <= start)
                return new(false, "The end must be after the start.", nameof(input.EndDate));

            if (input.Id == 0)
            {
                var error = CheckText(title, message);
                if (error is not null) return error;
                if (end <= DateTime.Now)
                    return new(false, "The end time has already passed.", nameof(input.EndDate));

                await _announcements.AddAsync(new Announcement
                {
                    Title = title,
                    Message = message,
                    StartDate = start,
                    EndDate = end,
                    Link = link,
                    CreatedByUserId = adminUserId
                });
                await _announcements.SaveChangesAsync();
                return new(true, $"Announcement \"{title}\" saved. It is not sent yet.");
            }

            var a = await _announcements.Query().FirstOrDefaultAsync(x => x.Id == input.Id);
            if (a is null) return new(false, "This announcement no longer exists.");

            var textError = CheckText(title, message);
            if (textError is not null) return textError;

            // A sent one can still be corrected: the banner follows, the emails already sent cannot change.
            a.Title = title;
            a.Message = message;
            a.Link = link;
            a.StartDate = start;
            a.EndDate = end;
            await _announcements.SaveChangesAsync();
            return new(true, a.SentAt is null
                ? $"Announcement \"{a.Title}\" updated."
                : $"\"{a.Title}\" updated on the home page. Customers keep the version they already received.");
        }

        // ======================= Send =======================

        public async Task<AnnouncementResult> SendAsync(int id)
        {
            var a = await _announcements.Query().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return new(false, "This announcement no longer exists.");
            if (a.SentAt is not null) return new(false, "This announcement was already sent.");
            if (!a.IsActive) return new(false, "This announcement is stopped. Resume it before sending.");
            if (a.EndDate < DateTime.Now) return new(false, "This announcement has ended and cannot be sent.");

            // Claim the send first: only one click (or tab) can change SentAt from null.
            DateTime? sentAt = DateTime.Now;
            var claimed = await _announcements.Query()
                .Where(x => x.Id == id && x.SentAt == null && x.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SentAt, sentAt));
            if (claimed == 0) return new(false, "This announcement was already sent.");

            var customers = await RecipientsAsync();

            // Email button: the page the admin picked. Customers already have an account,
            // so "Join YORU" (and no button) becomes a visit to the home page.
            var toHome = a.Link is AnnouncementLink.None or AnnouncementLink.SignUp;
            var buttonText = toHome ? "Visit YORU" : AnnouncementLinks.Text(a.Link);
            var buttonUrl = AbsoluteUrl(toHome ? "/" : AnnouncementLinks.Path(a.Link));
            var dates = Dates(a.StartDate, a.EndDate);
            var footer = await FooterAsync();
            var subject = $"YORU · {a.Title}";

            // One at a time (shared DbContext). Each step only logs on failure.
            foreach (var customer in customers)
            {
                // In-app: Promotion without an email, so NotificationService sends no email of its own.
                await _notifications.SendAsync(customer.Id, a.Title, a.Message, NotificationType.Promotion);

                if (string.IsNullOrWhiteSpace(customer.Email)) continue;
                try
                {
                    var html = AnnouncementEmail.Build(a.Title, a.Message, dates, customer.FullName,
                        buttonText, buttonUrl, footer);
                    await _email.SendEmailAsync(customer.Email, subject, html);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Announcement {Id} email to customer {UserId} was not sent.", id, customer.Id);
                }
            }

            await _announcements.Query()
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RecipientCount, customers.Count));

            _logger.LogInformation("Announcement {Id} sent to {Count} customers.", id, customers.Count);
            return new(true, customers.Count == 0
                ? "Sent, but there are no active customer accounts yet."
                : $"Sent to {customers.Count} customer{(customers.Count == 1 ? "" : "s")} (in-app and email).");
        }

        public async Task<int> CountRecipientsAsync() => (await RecipientsAsync()).Count;

        // Every active customer account (removed or deactivated accounts are skipped).
        private async Task<List<ApplicationUser>> RecipientsAsync() =>
            (await _users.GetUsersInRoleAsync(Roles.Customer))
                .Where(u => u.IsActive && u.RemovedAt is null)
                .ToList();

        // ======================= Stop / resume / delete =======================

        public Task<AnnouncementResult> StopAsync(int id) => SetActiveAsync(id, false);

        public Task<AnnouncementResult> ResumeAsync(int id) => SetActiveAsync(id, true);

        private async Task<AnnouncementResult> SetActiveAsync(int id, bool active)
        {
            var a = await _announcements.Query().FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return new(false, "This announcement no longer exists.");
            if (a.IsActive == active)
                return new(true, active ? "This announcement is already live." : "This announcement is already stopped.");

            a.IsActive = active;
            await _announcements.SaveChangesAsync();
            return new(true, active
                ? $"\"{a.Title}\" resumed. The banner shows again while its dates include today."
                : $"\"{a.Title}\" stopped. The home page banner is hidden.");
        }

        public async Task<AnnouncementResult> DeleteAsync(int id)
        {
            var a = await _announcements.Query().FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return new(false, "This announcement no longer exists.");
            if (a.SentAt is not null)
                return new(false, "A sent announcement is kept as a record. Stop it instead to hide the banner.");

            _announcements.Remove(a);
            await _announcements.SaveChangesAsync();
            return new(true, $"Announcement \"{a.Title}\" deleted.");
        }

        // ======================= Home page banner =======================

        public async Task<List<AnnouncementBannerItem>> GetRunningAsync(int take = AnnouncementBanner.MaxSlides)
        {
            var now = DateTime.Now;
            return await _announcements.Query()
                .AsNoTracking()
                .Where(a => a.IsActive && a.StartDate <= now && a.EndDate >= now)
                .OrderByDescending(a => a.StartDate)
                .ThenByDescending(a => a.Id)
                .Take(take)
                .Select(a => new AnnouncementBannerItem(a.Id, a.Title, a.Message, a.EndDate, a.Link))
                .ToListAsync();
        }

        // ======================= Helpers =======================

        private static AnnouncementState StateOf(Announcement a, DateTime now) =>
            !a.IsActive ? AnnouncementState.Stopped
            : a.EndDate < now ? AnnouncementState.Ended
            : a.StartDate > now ? AnnouncementState.Scheduled
            : AnnouncementState.Running;

        private static AnnouncementResult? CheckText(string title, string message)
        {
            if (title.Length == 0) return new(false, "Enter a title.", nameof(AnnouncementInput.Title));
            if (title.Length > 80) return new(false, "Keep the title within 80 characters.", nameof(AnnouncementInput.Title));
            if (message.Length == 0) return new(false, "Enter a message.", nameof(AnnouncementInput.Message));
            if (message.Length > 500) return new(false, "Keep the message within 500 characters.", nameof(AnnouncementInput.Message));
            return null;
        }

        // "/Menu" -> "https://localhost:7223/Menu". No web request -> no button.
        private string? AbsoluteUrl(string? path)
        {
            var request = _http.HttpContext?.Request;
            if (string.IsNullOrWhiteSpace(path) || request is null) return null;
            return $"{request.Scheme}://{request.Host}{request.PathBase}{path}";
        }

        // Same footer as the other YORU emails: name · address · phone.
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
                _logger.LogWarning(ex, "Could not read restaurant details for the announcement email footer.");
                return Brand.FullName;
            }
        }

        // "3 – 9 Nov 2026", "3 Nov 2026, 6 PM – 10:30 PM" (see AnnouncementTimes.Range).
        public static string Dates(DateTime start, DateTime end) => AnnouncementTimes.Range(start, end);
    }
}
