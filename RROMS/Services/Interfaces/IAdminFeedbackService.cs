using RROMS.Models;

namespace RROMS.Services.Interfaces
{
    // Owner: Member 5 (US43 View and reply to feedback).
    // Admin side of the feedback module, plus the customer's "My reviews" list.
    public interface IAdminFeedbackService
    {
        // Average rating, count per rating (1-5) and how many reviews still need a reply.
        Task<FeedbackSummary> GetSummaryAsync();

        // All feedback with its order and customer, filtered and sorted for the admin list.
        Task<List<Feedback>> GetFeedbackAsync(FeedbackFilter filter);

        // Saves (or updates) the admin's reply. Throws InvalidOperationException with a
        // user-friendly message if the reply is empty, too long or the feedback does not exist.
        // Returns the updated feedback (with its order) so the caller can notify the customer.
        Task<Feedback> ReplyAsync(int feedbackId, string? reply);

        // The logged-in customer's own feedback with the restaurant's replies, newest first.
        Task<List<Feedback>> GetCustomerFeedbackAsync(string userId);
    }
}
