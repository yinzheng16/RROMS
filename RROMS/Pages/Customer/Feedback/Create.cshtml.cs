using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer.Feedback;

public class Create : PageModel
{
    private readonly IFeedbackService _feedbackService;

    public Create(IFeedbackService feedbackService)
    {
        _feedbackService = feedbackService;
    }

    public List<Order> EligibleOrders { get; set; } = new();

    [BindProperty]
    [Required(ErrorMessage = "Please select an order.")]
    public int? OrderId { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Please select a rating.")]
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    public int? Rating { get; set; }

    [BindProperty]
    [StringLength(500, ErrorMessage = "Comment cannot exceed 500 characters.")]
    public string? Comment { get; set; }

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        EligibleOrders = await _feedbackService.GetEligibleOrdersAsync(userId);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            EligibleOrders = await _feedbackService.GetEligibleOrdersAsync(userId);
            return Page();
        }
        
        if (!OrderId.HasValue || !Rating.HasValue)
        {
            ModelState.AddModelError(
                string.Empty,
                "Please select an order and rating.");

            EligibleOrders =
                await _feedbackService.GetEligibleOrdersAsync(userId);

            return Page();
        }

        try
        {
            await _feedbackService.SubmitFeedbackAsync(
                userId,
                OrderId.Value,
                Rating.Value,
                Comment);

            TempData["SuccessMessage"] =
                "Thank you! Your feedback has been submitted.";

            return RedirectToPage();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);

            EligibleOrders =
                await _feedbackService.GetEligibleOrdersAsync(userId);

            return Page();
        }
    }
}