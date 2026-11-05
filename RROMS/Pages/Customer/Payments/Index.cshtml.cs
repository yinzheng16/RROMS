// Owner: Member 4 — US34 Customer payment records ("My payments").
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Customer.Payments;

// The /Customer folder already requires CustomerOnly (Program.cs);
// the attribute repeats it so the page stays protected if moved.
[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(IPaymentService payments, IReceiptService receipts, IOnlinePaymentService onlinePayments) : PageModel
{
    private readonly IPaymentService _payments = payments;

    // US33: bills still waiting for payment, shown as one line above the records.
    private readonly IOnlinePaymentService _onlinePayments = onlinePayments;

    // US35: PDF receipt for a Paid payment.
    private readonly IReceiptService _receipts = receipts;

    public static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-MY");

    // Filters stay in the URL (?from=2026-10-01&to=2026-10-30),
    // so refreshing or sharing the link keeps them. No customer ID in the URL.
    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public CustomerPaymentHistory History { get; private set; } = new();

    // US33: "You have N bills to pay" line (0 = line hidden).
    public int BillsToPay { get; private set; }
    public decimal BillsToPayTotal { get; private set; }

    public bool IsFiltered => From.HasValue || To.HasValue;

    // Latest date the date pickers allow (today, server time).
    public string TodayIso => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<IActionResult> OnGetAsync()
    {
        // The customer always comes from the login, never from the query string.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        // US33: confirmed bills (dine-in) and takeaway orders the customer can pay online.
        // Shown only when online payment is set up; "pay at counter" items are left out.
        if (_onlinePayments.IsOnlinePaymentAvailable)
        {
            var due = (await _onlinePayments.GetPayableItemsAsync(userId))
                .Where(i => i.PayStatus is TakeawayPayStatus.CanPay or TakeawayPayStatus.InProgress)
                .ToList();
            BillsToPay = due.Count;
            BillsToPayTotal = due.Sum(i => i.Amount);
        }

        // A date that cannot be read (e.g. ?from=abc) is a model binding error.
        if (!ModelState.IsValid)
        {
            ModelState.Clear();
            ModelState.AddModelError(string.Empty, "Please enter valid dates.");
            From = null;
            To = null;
        }

        // Server-side check (the date pickers check this in the browser too).
        if (From is DateOnly f && To is DateOnly t && f > t)
        {
            ModelState.AddModelError(nameof(To), "The To date cannot be before the From date.");
            return Page();   // keep the entered dates, show no list
        }

        History = await _payments.GetCustomerPaymentsAsync(userId, From, To);
        return Page();
    }

    // US35: Download the PDF receipt of one of the customer's own Paid payments.
    // /Customer/Payments?handler=Receipt&id=12
    // Not found, not Paid or someone else's payment: all give 404.
    public async Task<IActionResult> OnGetReceiptAsync(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var receipt = await _receipts.GetCustomerReceiptAsync(userId, id);
        if (receipt is null) return NotFound();

        var pdf = _receipts.CreatePdf(receipt);
        return File(pdf, "application/pdf", $"YORU-receipt-{receipt.ReceiptNo}.pdf");
    }

    // ---- Display helpers ----

    public static string DateIso(DateOnly? d) =>
        d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    public static string Money(decimal amount) =>
        "RM " + amount.ToString("N2", CultureInfo.InvariantCulture);

    public static string When(DateTime at) =>
        at.ToString("d MMM yyyy, h:mm tt", DisplayCulture);

    public static string MethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Cash",
        PaymentMethod.Card => "Card",
        PaymentMethod.Online => "Online",
        _ => method.ToString()
    };

    // "Dine-in · Table T05" / "Takeaway · YT-20261030-001"
    public static string WhatLabel(CustomerPaymentRecord p) => p.OrderType == OrderType.DineIn
        ? "Dine-in" + (string.IsNullOrWhiteSpace(p.TableNumber) ? "" : " · Table " + p.TableNumber)
        : "Takeaway" + (string.IsNullOrWhiteSpace(p.OrderNo) ? "" : " · " + p.OrderNo);

    // Lower-case key used for the coloured status text (status-paid, status-pending ...).
    public static string StatusKey(PaymentStatus status) => status.ToString().ToLowerInvariant();
}
