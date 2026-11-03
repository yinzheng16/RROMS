namespace RROMS.Services.Interfaces
{
    // ---------- US10 (Owner: Member 1): Admin → Restaurant → Chatbot FAQs ----------

    // Status: "shown" / "hidden" / null = all.
    public record FaqListQuery(string? Search, string? Status, int Page);

    public record FaqRow(int Id, int Number, string Question, string Answer, IReadOnlyList<string> Keywords, bool IsActive);

    public record FaqStats(int All, int Shown, int Hidden);

    public record FaqListPage(IReadOnlyList<FaqRow> Rows, int Matches, int Page, int PageCount, int PageSize, FaqStats Stats);

    // Id = 0 adds a new FAQ, otherwise edits it. Keywords: "parking, car park".
    public record FaqInput(int Id, string? Question, string? Answer, string? Keywords, bool IsActive);

    // Field: "Question", "Answer" or "Keywords" for a message under that box; null for a toast.
    public record FaqResult(bool Ok, string Message, string? Field = null, int? Id = null);

    public interface IChatFaqService
    {
        // FAQs in the order the chatbot lists them (oldest first), 8 per page. Number = position in that order.
        Task<FaqListPage> GetFaqsAsync(FaqListQuery query);

        Task<FaqResult> SaveAsync(FaqInput input);

        // Shown in the chatbot or hidden (hidden FAQs are kept but never used).
        Task<FaqResult> SetShownAsync(int id, bool shown);

        Task<FaqResult> DeleteAsync(int id);
    }
}
