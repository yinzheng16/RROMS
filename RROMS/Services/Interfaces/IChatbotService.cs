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
    public record ChatReply(string Text, string Source, int? FaqId = null, ChatHoursCard? Hours = null);

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
    }
}
