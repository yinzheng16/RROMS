namespace RROMS.Services.Interfaces
{
    // ---------- US08 (Owner: Member 1): AI chatbot on the customer pages ----------

    // One earlier message in the conversation. Role: "user" or "bot".
    public record ChatTurn(string Role, string Text);

    // One day in the opening-hours card. IsToday highlights the row.
    public record ChatHoursRow(string Day, string Hours, bool IsToday, bool IsClosed);

    // Opening hours card: status line ("Open now · last orders 11:30 PM"), 7 days, and upcoming special dates.
    public record ChatHoursCard(string Status, IReadOnlyList<ChatHoursRow> Days, IReadOnlyList<string> Specials);

    // A shown FAQ for the list inside the chat (and the answer when it is tapped).
    public record ChatFaqItem(int Id, string Question, string Answer);

    // The bot's reply.
    // Text: short answer; may contain **bold**, line breaks, "- " bullets and [links](/Menu) to our own pages.
    // Source: "ai" (answered by the AI), "faq" (an FAQ matched by keywords), "help" (built-in answer),
    //         "limit" (asked too many questions), "error".
    // FaqId: the FAQ the answer came from, when known. Hours: attach the hours card.
    // Check: a table availability result (US09). BookingForm: show the small date / time / guests form in the chat.
    public record ChatReply(string Text, string Source, int? FaqId = null, ChatHoursCard? Hours = null,
                            ChatAvailability? Check = null, bool BookingForm = false);

    // ---------- US09 (Owner: Member 1): table availability in the chat + booking link ----------

    // A table type that is free at that time (TablesLeft) and its minimum spend (0 = none).
    public record ChatTableType(string Name, string? JapaneseName, int TablesLeft, decimal MinimumSpend);

    // Another time to try ("7:30 PM", "Sun 9 Nov · 8:00 PM"). Date "yyyy-MM-dd", Time "HH:mm".
    public record ChatTimeOption(string Date, string Time, string Label);

    // The result of checking one date, time and party size with Member 2's booking rules.
    // Valid = the time can be booked at all (open, not in the past, not in a break...).
    // BookUrl = the booking page already filled in with this date, time and guests (only when Available).
    public record ChatAvailability(
        bool Valid, bool Available, string Date, string Time, int Guests, string When, string? Message,
        IReadOnlyList<ChatTableType> Types, IReadOnlyList<ChatTimeOption> Suggestions, string? BookUrl);

    // What the small booking form in the chat offers: bookable dates and the largest party size.
    public record ChatDateOption(string Value, string Label);
    public record ChatBookingSetup(IReadOnlyList<ChatDateOption> Dates, int MaxGuests);

    public interface IChatbotService
    {
        // true when an API key is set (otherwise answers come from FAQ keywords only).
        bool AiConfigured { get; }

        // A customer's question. History = the last few messages of this chat (the browser sends them).
        // visitorKey (e.g. the IP address) limits how many questions one visitor can ask in a few minutes.
        Task<ChatReply> AskAsync(string? question, IReadOnlyList<ChatTurn>? history, string visitorKey);

        // Admin "Try the assistant" (US10): same answer as a customer would get, no question limit.
        Task<ChatReply> TryAsync(string? question);

        // The shown FAQs, in admin order (US10 decides what appears here).
        Task<IReadOnlyList<ChatFaqItem>> GetFaqsAsync();

        // The opening-hours card (Member 2's opening hours + special dates).
        Task<ChatHoursCard> GetHoursAsync();

        // ---------- US09 ----------

        // Is there a table for this many guests at this time? date "yyyy-MM-dd", time "HH:mm".
        // Uses Member 2's IReservationService.CheckTimeAsync / GetTypeOptionsAsync (the same rules as the booking page).
        Task<ChatAvailability> CheckAsync(string? date, string? time, int guests);

        // Bookable dates (same 60 days as the booking page) and the largest party size, for the form in the chat.
        Task<ChatBookingSetup> GetBookingSetupAsync();

        // The times that can be booked on one date ("HH:mm" + "7:30 PM"), for the form in the chat.
        Task<IReadOnlyList<ChatTimeOption>> GetTimesAsync(string? date);
    }
}
