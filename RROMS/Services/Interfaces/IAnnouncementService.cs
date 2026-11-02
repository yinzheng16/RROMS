using System.ComponentModel.DataAnnotations;
using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // US39 (Member 4): announcements and promotional campaigns.
    // Admin page: /Admin/Announcements. Home page banner: Pages/Shared/Components/AnnouncementBanner.
    public interface IAnnouncementService
    {
        // Admin list, newest first, with the state worked out from today's date.
        Task<List<AnnouncementRow>> GetAllAsync();

        // For the edit form; null = not found.
        Task<AnnouncementInput?> GetInputAsync(int id);

        // "Copy": a new, unsent announcement with the same text and button (dates moved to today if they passed).
        Task<AnnouncementInput?> GetCopyAsync(int id);

        // Id = 0: new announcement; otherwise edit. A sent one can still be edited: the change shows on the
        // home page banner, but customers keep the version they received (emails cannot be changed).
        // To tell customers again, copy it (GetCopyAsync) and send the copy.
        Task<AnnouncementResult> SaveAsync(AnnouncementInput input, string adminUserId);

        // Sends it once to every active customer: in-app (US37) + email (US38).
        // A second click / second tab is refused (SentAt is claimed with a conditional update).
        Task<AnnouncementResult> SendAsync(int id);

        // Stop hides the banner; Resume shows it again (dates permitting).
        Task<AnnouncementResult> StopAsync(int id);
        Task<AnnouncementResult> ResumeAsync(int id);

        // Only announcements that were never sent can be deleted; sent ones are kept as a record (stop them instead).
        Task<AnnouncementResult> DeleteAsync(int id);

        // How many customers a Send reaches now (active Customer accounts), for the confirm pop-up.
        Task<int> CountRecipientsAsync();

        // Home page banner: active announcements whose dates include today, newest first.
        Task<List<AnnouncementBannerItem>> GetRunningAsync(int take = AnnouncementBanner.MaxSlides);
    }

    public enum AnnouncementState { Scheduled, Running, Ended, Stopped }

    public record AnnouncementRow(
        int Id,
        string Title,
        string Message,
        DateTime StartDate,
        DateTime EndDate,
        AnnouncementState State,
        DateTime CreatedAt,
        DateTime? SentAt,
        int RecipientCount,
        AnnouncementLink Link)
    {
        public bool IsSent => SentAt.HasValue;

        // Can be sent: never sent, not stopped, not ended (a scheduled one may be sent early, e.g. "coming next week").
        public bool CanSend => !IsSent && (State == AnnouncementState.Running || State == AnnouncementState.Scheduled);
    }

    public record AnnouncementBannerItem(int Id, string Title, string Message, DateTime EndDate, AnnouncementLink Link);

    // US39: how start / end times are written (admin list, banner, email). Times are kept to the minute.
    public static class AnnouncementTimes
    {
        private static readonly System.Globalization.CultureInfo En = System.Globalization.CultureInfo.InvariantCulture;

        public static DateTime Minute(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0);

        // Starts at midnight and ends at 11:59 PM: whole days, so no times are shown.
        private static bool WholeDays(DateTime start, DateTime end) =>
            start.TimeOfDay == TimeSpan.Zero && end.TimeOfDay >= new TimeSpan(23, 59, 0);

        private static string Time(DateTime t) => t.ToString(t.Minute == 0 ? "h tt" : "h:mm tt", En);

        // "3 – 9 Nov 2026" (whole days), "3 Nov 2026, 6 PM – 10:30 PM", "3 Nov, 6 PM – 9 Nov 2026, 11:59 PM".
        public static string Range(DateTime start, DateTime end)
        {
            if (WholeDays(start, end))
                return start.Date == end.Date ? start.ToString("d MMM yyyy", En)
                    : start.Year == end.Year && start.Month == end.Month ? $"{start.ToString("%d", En)} – {end.ToString("d MMM yyyy", En)}"
                    : start.Year == end.Year ? $"{start.ToString("d MMM", En)} – {end.ToString("d MMM yyyy", En)}"
                    : $"{start.ToString("d MMM yyyy", En)} – {end.ToString("d MMM yyyy", En)}";

            if (start.Date == end.Date)
                return $"{start.ToString("d MMM yyyy", En)}, {Time(start)} – {Time(end)}";

            var startDay = start.Year == end.Year ? start.ToString("d MMM", En) : start.ToString("d MMM yyyy", En);
            return $"{startDay}, {Time(start)} – {end.ToString("d MMM yyyy", En)}, {Time(end)}";
        }

        // Banner / admin side list: "9 Nov" when it runs to the end of the day, otherwise "9 Nov, 10 PM".
        public static string Until(DateTime end) =>
            end.TimeOfDay >= new TimeSpan(23, 59, 0) ? end.ToString("d MMM", En) : $"{end.ToString("d MMM", En)}, {Time(end)}";
    }

    // US39: home page banner rules (shared by the banner and the admin preview).
    public static class AnnouncementBanner
    {
        public const int MaxSlides = 5;        // newest 5 live announcements take turns
        public const int SlideSeconds = 6;     // each shows for 6 seconds, then slides left
    }

    // US39: button text and page for each AnnouncementLink (banner, email and admin form use the same words).
    public static class AnnouncementLinks
    {
        public static string? Text(AnnouncementLink link) => link switch
        {
            AnnouncementLink.Menu => "View menu",
            AnnouncementLink.BookTable => "Book a table",
            AnnouncementLink.SignUp => "Join YORU",
            _ => null
        };

        public static string? Path(AnnouncementLink link) => link switch
        {
            AnnouncementLink.Menu => "/Menu",
            AnnouncementLink.BookTable => "/Customer/Reservations/Create",
            AnnouncementLink.SignUp => "/Identity/Account/Register",
            _ => null
        };

        // Shown in the admin form's drop-down.
        public static string Option(AnnouncementLink link) => link switch
        {
            AnnouncementLink.Menu => "Menu · “View menu”",
            AnnouncementLink.BookTable => "Book a table · “Book a table”",
            AnnouncementLink.SignUp => "Sign up, visitors only · “Join YORU”",
            _ => "No button"
        };
    }

    // Field = the input that is wrong (to show the message under it), or null.
    public record AnnouncementResult(bool Success, string Message, string? Field = null);

    public class AnnouncementInput
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Enter a title.")]
        [StringLength(80, ErrorMessage = "Keep the title within 80 characters.")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a message.")]
        [StringLength(500, ErrorMessage = "Keep the message within 500 characters.")]
        public string Message { get; set; } = string.Empty;

        // Date and time. New: from now until 11:59 PM a week later.
        [DataType(DataType.DateTime)]
        public DateTime StartDate { get; set; } = AnnouncementTimes.Minute(DateTime.Now);

        [DataType(DataType.DateTime)]
        public DateTime EndDate { get; set; } = DateTime.Today.AddDays(8).AddMinutes(-1);

        public AnnouncementLink Link { get; set; } = AnnouncementLink.None;

        // Shown on the form: already sent, so edits only change the home page banner.
        public bool IsSent { get; set; }
    }
}
