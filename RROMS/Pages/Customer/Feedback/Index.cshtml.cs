using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer.Feedback;

// US43 "Reply visible to customer" (Member 5): the customer's own reviews with the restaurant's replies.
// Customer only (the /Customer folder is protected in Program.cs).
public class Index : PageModel
{
    private readonly IAdminFeedbackService _feedbackService;

    public Index(IAdminFeedbackService feedbackService)
    {
        _feedbackService = feedbackService;
    }

    public List<RROMS.Models.Feedback> Reviews { get; private set; } = new();

    public int RepliedCount => Reviews.Count(r => !string.IsNullOrWhiteSpace(r.AdminReply));

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        Reviews = await _feedbackService.GetCustomerFeedbackAsync(userId);
    }
}
