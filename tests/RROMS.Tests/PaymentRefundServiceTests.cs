using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Tests;

// QA01 (Member 4): US55 refunds (US54 in code and GitHub issue #152).
// Full refunds of Paid payments only, with a reason; online payments go
// through Stripe first, and nothing is saved if Stripe refuses.
public class PaymentRefundServiceTests : IDisposable
{
    private const decimal Amount = 25.00m;

    private readonly SqliteTestDb _test = new();
    private readonly FakeStripeCheckoutGateway _stripe = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly PaymentRefundService _service;

    public PaymentRefundServiceTests()
    {
        _service = new PaymentRefundService(
            new Repository<Payment>(_test.Db),
            new FakeBillingTransactionRepository(),
            _stripe,
            _notifications,
            NullLogger<PaymentRefundService>.Instance);
    }

    // A paid takeaway bill with one payment of RM 25.00.
    private async Task<(ApplicationUser Customer, Bill Bill, Payment Payment)> PaidAsync(PaymentMethod method)
    {
        var customer = await TestData.CreateUserAsync(_test.Db);
        var item = await TestData.CreateMenuItemAsync(_test.Db);
        var (bill, _) = await TestData.CreateTakeawayBillAsync(
            _test.Db, customer.Id, item, Amount, BillStatus.Paid);

        string? reference = null;

        if (method == PaymentMethod.Online)
            reference = "cs_test_" + Guid.NewGuid().ToString("N")[..16];

        var payment = await TestData.CreatePaymentAsync(
            _test.Db, bill.Id, Amount, method, PaymentStatus.Paid, reference);

        if (reference != null)
            _stripe.Sessions[reference] = FakeStripeCheckoutGateway.Paid(reference, 2500, payment.Id);

        return (customer, bill, payment);
    }

    private Task<Payment> ReadPaymentAsync(int id) =>
        _test.Db.Payments.AsNoTracking().SingleAsync(p => p.Id == id);

    [Fact]
    public async Task CashRefund_SavesRefundDetails_AndNotifiesCustomer()
    {
        var (customer, bill, payment) = await PaidAsync(PaymentMethod.Cash);

        await _service.RefundAsync(payment.Id, "admin-1",
            RefundReasons.CustomerComplaint, null, moneyReturned: true);

        var saved = await ReadPaymentAsync(payment.Id);
        var savedBill = await _test.Db.Bills.AsNoTracking().SingleAsync(b => b.Id == bill.Id);
        var notice = Assert.Single(_notifications.SentNotifications);

        Assert.Equal(PaymentStatus.Refunded, saved.Status);
        Assert.NotNull(saved.RefundedAt);
        Assert.Equal("admin-1", saved.RefundedByUserId);
        Assert.Equal(RefundReasons.CustomerComplaint, saved.RefundReason);
        Assert.Equal(BillStatus.Paid, savedBill.Status);   // the bill stays Paid
        Assert.Equal(customer.Id, notice.UserId);
        Assert.Equal(NotificationType.Payment, notice.Type);
    }

    [Fact]
    public async Task CashRefund_MoneyNotReturned_IsRefused()
    {
        var (_, _, payment) = await PaidAsync(PaymentMethod.Cash);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.RefundAsync(payment.Id, "admin-1",
                RefundReasons.OrderProblem, null, moneyReturned: false));

        Assert.Equal(PaymentStatus.Paid, (await ReadPaymentAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task OtherReasonWithoutNote_IsRefused()
    {
        var (_, _, payment) = await PaidAsync(PaymentMethod.Cash);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.RefundAsync(payment.Id, "admin-1",
                RefundReasons.Other, "   ", moneyReturned: true));

        Assert.Equal(PaymentStatus.Paid, (await ReadPaymentAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task PaymentCanBeRefundedOnlyOnce()
    {
        var (_, _, payment) = await PaidAsync(PaymentMethod.Cash);

        await _service.RefundAsync(payment.Id, "admin-1",
            RefundReasons.ChargedTwice, null, moneyReturned: true);

        var first = await ReadPaymentAsync(payment.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RefundAsync(payment.Id, "admin-2",
                RefundReasons.ChargedTwice, null, moneyReturned: true));

        var after = await ReadPaymentAsync(payment.Id);

        Assert.Equal(first.RefundedAt, after.RefundedAt);
        Assert.Equal("admin-1", after.RefundedByUserId);
    }

    [Fact]
    public async Task PendingPayment_CannotBeRefunded()
    {
        var customer = await TestData.CreateUserAsync(_test.Db);
        var item = await TestData.CreateMenuItemAsync(_test.Db);
        var (bill, _) = await TestData.CreateTakeawayBillAsync(
            _test.Db, customer.Id, item, Amount, BillStatus.Confirmed);
        var payment = await TestData.CreatePaymentAsync(
            _test.Db, bill.Id, Amount, PaymentMethod.Online, PaymentStatus.Pending, "cs_test_pending");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RefundAsync(payment.Id, "admin-1",
                RefundReasons.CustomerComplaint, null, moneyReturned: false));

        Assert.Equal(PaymentStatus.Pending, (await ReadPaymentAsync(payment.Id)).Status);
        Assert.Empty(_stripe.RefundRequests);
    }

    [Fact]
    public async Task OnlineRefund_AsksStripeForTheFullAmount()
    {
        var (_, _, payment) = await PaidAsync(PaymentMethod.Online);

        await _service.RefundAsync(payment.Id, "admin-1",
            RefundReasons.CustomerComplaint, null, moneyReturned: false);

        var request = Assert.Single(_stripe.RefundRequests);

        Assert.Equal(2500, request.AmountInSen);
        Assert.Equal("pi_test_" + payment.Id, request.PaymentIntentId);
        Assert.Equal(PaymentStatus.Refunded, (await ReadPaymentAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task OnlineRefund_StripeRefuses_NothingIsSaved()
    {
        var (_, _, payment) = await PaidAsync(PaymentMethod.Online);
        _stripe.RefundStatus = "failed";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RefundAsync(payment.Id, "admin-1",
                RefundReasons.CustomerComplaint, null, moneyReturned: false));

        var saved = await ReadPaymentAsync(payment.Id);

        Assert.Equal(PaymentStatus.Paid, saved.Status);
        Assert.Null(saved.RefundedAt);
        Assert.Null(saved.RefundReason);
        Assert.Empty(_notifications.SentNotifications);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotUndoTheRefund()
    {
        var (_, _, payment) = await PaidAsync(PaymentMethod.Cash);
        _notifications.Throw = true;

        await _service.RefundAsync(payment.Id, "admin-1",
            RefundReasons.OrderProblem, null, moneyReturned: true);

        Assert.Equal(PaymentStatus.Refunded, (await ReadPaymentAsync(payment.Id)).Status);
    }

    public void Dispose() => _test.Dispose();
}
