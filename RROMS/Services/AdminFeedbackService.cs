using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    // Owner: Member 5 (US43 View and reply to feedback).
    public class AdminFeedbackService : IAdminFeedbackService
    {
        public const int MaxReplyLength = 500;

        private readonly IRepository<Feedback> _feedbackRepository;

        public AdminFeedbackService(IRepository<Feedback> feedbackRepository)
        {
            _feedbackRepository = feedbackRepository;
        }

        public async Task<FeedbackSummary> GetSummaryAsync()
        {
            var rows = await _feedbackRepository.Query()
                .AsNoTracking()
                .Select(f => new { f.Rating, f.AdminReply })
                .ToListAsync();

            if (rows.Count == 0)
            {
                return FeedbackSummary.Empty;
            }

            var counts = new int[6];
            foreach (var row in rows)
            {
                if (row.Rating >= 1 && row.Rating <= 5)
                {
                    counts[row.Rating]++;
                }
            }

            var average = Math.Round(rows.Average(r => r.Rating), 1);
            var needsReply = rows.Count(r => string.IsNullOrWhiteSpace(r.AdminReply));

            return new FeedbackSummary(rows.Count, average, counts, needsReply);
        }

        public async Task<List<Feedback>> GetFeedbackAsync(FeedbackFilter filter)
        {
            var query = _feedbackRepository.Query()
                .AsNoTracking()
                .Include(f => f.Order)
                    .ThenInclude(o => o.User)
                .AsQueryable();

            // Filter by rating (AC: "Filter by rating")
            if (filter.Rating is >= 1 and <= 5)
            {
                query = query.Where(f => f.Rating == filter.Rating.Value);
            }

            // Filter by reply status
            if (filter.Status == "needs")
            {
                query = query.Where(f => f.AdminReply == null || f.AdminReply == "");
            }
            else if (filter.Status == "replied")
            {
                query = query.Where(f => f.AdminReply != null && f.AdminReply != "");
            }

            // Search order number, customer name or comment
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var term = filter.Search.Trim();
                query = query.Where(f =>
                    f.Order.OrderNo.Contains(term) ||
                    f.Order.User.FullName.Contains(term) ||
                    (f.Comment != null && f.Comment.Contains(term)));
            }

            query = filter.Sort == "lowest"
                ? query.OrderBy(f => f.Rating).ThenByDescending(f => f.CreatedAt)
                : query.OrderByDescending(f => f.CreatedAt);

            return await query.ToListAsync();
        }

        public async Task<Feedback> ReplyAsync(int feedbackId, string? reply)
        {
            var text = reply?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                throw new InvalidOperationException("Please write a reply before sending.");
            }

            if (text.Length > MaxReplyLength)
            {
                throw new InvalidOperationException($"Reply cannot exceed {MaxReplyLength} characters.");
            }

            var feedback = await _feedbackRepository.Query()
                .Include(f => f.Order)
                .FirstOrDefaultAsync(f => f.Id == feedbackId);

            if (feedback == null)
            {
                throw new InvalidOperationException("This feedback could not be found.");
            }

            feedback.AdminReply = text;
            _feedbackRepository.Update(feedback);
            await _feedbackRepository.SaveChangesAsync();

            return feedback;
        }

        public async Task<List<Feedback>> GetCustomerFeedbackAsync(string userId)
        {
            return await _feedbackRepository.Query()
                .AsNoTracking()
                .Include(f => f.Order)
                .Where(f => f.Order.UserId == userId)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();
        }
    }
}
