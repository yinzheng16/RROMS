using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services;

public class FeedbackService : IFeedbackService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Feedback> _feedbackRepository;

    public FeedbackService(
        IRepository<Order> orderRepository,
        IRepository<Feedback> feedbackRepository)
    {
        _orderRepository = orderRepository;
        _feedbackRepository = feedbackRepository;
    }

    public async Task<List<Order>> GetEligibleOrdersAsync(string userId)
    {
        return await _orderRepository.Query()
            .AsNoTracking()
            .Where(o =>
                o.UserId == userId &&
                o.Status == OrderStatus.Completed &&
                o.Feedback == null)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<Feedback?> GetFeedbackByOrderAsync(int orderId)
    {
        return await _feedbackRepository.Query()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.OrderId == orderId);
    }

    public async Task SubmitFeedbackAsync(
        string userId,
        int orderId,
        int rating,
        string? comment)
    {
        var order = await _orderRepository.Query()
            .Include(o => o.Feedback)
            .FirstOrDefaultAsync(o =>
                o.Id == orderId &&
                o.UserId == userId);

        if (order == null)
        {
            throw new InvalidOperationException(
                "The selected order could not be found.");
        }

        if (order.Status != OrderStatus.Completed)
        {
            throw new InvalidOperationException(
                "Feedback can only be submitted for completed orders.");
        }

        if (order.Feedback != null)
        {
            throw new InvalidOperationException(
                "Feedback has already been submitted for this order.");
        }

        if (rating < 1 || rating > 5)
        {
            throw new InvalidOperationException(
                "Rating must be between 1 and 5.");
        }

        if (!string.IsNullOrWhiteSpace(comment) && comment.Length > 500)
        {
            throw new InvalidOperationException(
                "Comment cannot exceed 500 characters.");
        }

        var feedback = new Feedback
        {
            OrderId = orderId,
            Rating = rating,
            Comment = string.IsNullOrWhiteSpace(comment)
                ? null
                : comment.Trim(),
            CreatedAt = DateTime.Now
        };

        await _feedbackRepository.AddAsync(feedback);
        await _feedbackRepository.SaveChangesAsync();
    }
}