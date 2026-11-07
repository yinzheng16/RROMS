using RROMS.Models;

namespace RROMS.Services.Interfaces;

public interface IFeedbackService
{
    Task<List<Order>> GetEligibleOrdersAsync(string userId);

    Task<Feedback?> GetFeedbackByOrderAsync(int orderId);

    Task SubmitFeedbackAsync(
        string userId,
        int orderId,
        int rating,
        string? comment);
}