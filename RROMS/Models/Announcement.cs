using System.ComponentModel.DataAnnotations;

namespace RROMS.Models
{
    // Owner: Member 4 (US39 Announcements and promotional campaigns)
    // An admin writes a short announcement (news or an offer) for a date range.
    //   - Home page: shown as a banner from StartDate to EndDate, date AND time (while IsActive).
    //   - Send: every active customer gets it once, in-app (US37) + email (US38). SentAt stops a second send.
    // Not the same as Member 3's Promotion (a discounted price on one dish); nothing here changes prices.
    public class Announcement
    {
        public int Id { get; set; }

        [Required, StringLength(80)]
        public string Title { get; set; } = string.Empty;

        // Kept within the in-app notification limit (500), so the customer sees the whole text.
        [Required, StringLength(500)]
        public string Message { get; set; } = string.Empty;

        // Date and time (minutes): shown from StartDate until EndDate, e.g. 3 Nov 6:00 PM – 9 Nov 11:59 PM.
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // false = stopped by the admin: the banner is hidden (already sent notifications stay).
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Required]
        public string CreatedByUserId { get; set; } = string.Empty;

        // Set when it was sent to customers; null = not sent yet. Only one send per announcement.
        public DateTime? SentAt { get; set; }

        // How many customers it was sent to (shown on the admin page).
        public int RecipientCount { get; set; }

        // Where the banner button (and the email button) leads. Saved as the word, e.g. "Menu".
        public AnnouncementLink Link { get; set; } = AnnouncementLink.None;
    }

    // US39: the page an announcement sends people to. The button text is fixed per page,
    // so the admin only picks one. Never rename a value (saved as the word in the database).
    public enum AnnouncementLink
    {
        None,
        Menu,
        Specials,   // Menu's Specials list (same as Home's "See all specials")
        BookTable,
        SignUp      // visitors only: hidden on the banner for logged-in users, not used in emails
    }
}
