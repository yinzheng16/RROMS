using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Tests;

// QA01 (Member 4): US33 online payment with Stripe Checkout (test mode).
// The fake gateway plays Stripe, so the tests decide whether a Checkout
// Session is paid, open, expired, or paid with the wrong amount/currency.
public class OnlinePaymentServiceTests : IDisposable
{
    private const decimal BillTotal = 25.00m;
    private const long BillTotalInSen = 2500;

    private static readonly OnlineCheckoutUrls Urls = new(
        "https://localhost/Customer/Payments/PayResult?session_id=" + OnlineCheckoutUrls.SessionIdToken,
        "https://localhost/Customer/Payments/PayResult?cancelled=" + OnlineCheckoutUrls.PaymentIdToken);

    private readonly SqliteTestDb _test = new();
    private readonly FakeStripeCheckoutGateway _stripe = new();
    private readonly OnlinePaymentService _service;

    public OnlinePaymentServiceTests()
    {
        var db = _test.Db;
        var lockRepo = new FakeBillingTransactionRepository();

        var billing = new BillingService(
            new Repository<Order>(db),
            new Repository<Payment>(db),
            new Repository<Bill>(db),
            new Repository<DiningSession>(db),
            lockRepo);

        _service = new OnlinePaymentService(
            new Repository<Payment>(db),
            new Repository<Order>(db),
            new Repository<Bill>(db),
            billing,
            lockRepo,
            _stripe,
            Options.Create(new StripeSettings
            {
                SecretKey = "sk_test_qa01",
                Currency = "myr",
                CheckoutMinutes = 30
            }),
            NullLogger<OnlinePaymentService>.Instance);
    }

    // A confirmed takeaway bill of RM 25.00 and its customer.
    private async Task<(ApplicationUser Customer, Bill Bill)> ConfirmedBillAsync(decimal total = BillTotal)
    {
        var customer = await TestData.CreateUserAsync(_test.Db);
        var item = await TestData.CreateMenuItemAsync(_test.Db);
        var (bill, _) = await TestData.CreateTakeawayBillAsync(
            _test.Db, customer.Id, item, total, BillStatus.Confirmed);
        return (customer, bill);
    }

    // An online payment already sent to Stripe (Pending, with a session id).
    private async Task<Payment> PendingOnlinePaymentAsync(int billId, DateTime? createdAt = null)
    {
        string sessionId = "cs_test_" + Guid.NewGuid().ToString("N")[..16];
        return await TestData.CreatePaymentAsync(_test.Db, billId, BillTotal,
            PaymentMethod.Online, PaymentStatus.Pending, sessionId, createdAt);
    }

    private Task<Payment> ReadPaymentAsync(int id) =>
        _test.Db.Payments.AsNoTracking().SingleAsync(p => p.Id == id);

    private Task<Bill> ReadBillAsync(int id) =>
        _test.Db.Bills.AsNoTracking().SingleAsync(b => b.Id == id);

    [Fact]
    public async Task StartCheckout_SavesPendingPaymentBeforeRedirect()
    {
        var (customer, bill) = await ConfirmedBillAsync();

        string url = await _service.StartBillCheckoutAsync(bill.Id, customer.Id, Urls);

        var payment = await _test.Db.Payments.AsNoTracking().SingleAsync(p => p.BillId == bill.Id);
        var request = Assert.Single(_stripe.CreatedRequests);

        Assert.StartsWith("https://checkout.stripe.test/", url);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(PaymentMethod.Online, payment.Method);
        Assert.Equal(BillTotal, payment.Amount);
        Assert.True(_stripe.Sessions.ContainsKey(payment.TransactionRef!));
        Assert.Equal(BillTotalInSen, request.AmountInSen);
        Assert.Equal("myr", request.Currency);
    }

    [Fact]
    public async Task StartCheckout_BillNotConfirmed_IsRefused()
    {
        var customer = await TestData.CreateUserAsync(_test.Db);
        var item = await TestData.CreateMenuItemAsync(_test.Db);
        var (bill, _) = await TestData.CreateTakeawayBillAsync(
            _test.Db, customer.Id, item, BillTotal, BillStatus.Draft);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.StartBillCheckoutAsync(bill.Id, customer.Id, Urls));

        Assert.Empty(_stripe.CreatedRequests);
        Assert.False(await _test.Db.Payments.AnyAsync());
    }

    [Fact]
    public async Task StartCheckout_BelowStripeMinimum_IsRefused()
    {
        var (customer, bill) = await ConfirmedBillAsync(total: 1.50m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.StartBillCheckoutAsync(bill.Id, customer.Id, Urls));

        Assert.Empty(_stripe.CreatedRequests);
        Assert.False(await _test.Db.Payments.AnyAsync());
    }

    [Fact]
    public async Task PaidSession_MarksPaymentAndBillPaid()
    {
        var (customer, bill) = await ConfirmedBillAsync();
        var payment = await PendingOnlinePaymentAsync(bill.Id);
        _stripe.Sessions[payment.TransactionRef!] =
            FakeStripeCheckoutGateway.Paid(payment.TransactionRef!, BillTotalInSen, payment.Id);

        var outcome = await _service.CompleteCheckoutAsync(payment.TransactionRef!, customer.Id);

        Assert.Equal(OnlinePaymentState.Paid, outcome.State);
        Assert.Equal(PaymentStatus.Paid, (await ReadPaymentAsync(payment.Id)).Status);
        Assert.NotNull((await ReadPaymentAsync(payment.Id)).PaidAt);
        Assert.Equal(BillStatus.Paid, (await ReadBillAsync(bill.Id)).Status);
    }

    [Fact]
    public async Task RepeatedCallback_RecordsThePaymentOnlyOnce()
    {
        var (customer, bill) = await ConfirmedBillAsync();
        var payment = await PendingOnlinePaymentAsync(bill.Id);
        _stripe.Sessions[payment.TransactionRef!] =
            FakeStripeCheckoutGateway.Paid(payment.TransactionRef!, BillTotalInSen, payment.Id);

        await _service.CompleteCheckoutAsync(payment.TransactionRef!, customer.Id);
        var firstPaidAt = (await ReadPaymentAsync(payment.Id)).PaidAt;

        // The success page is reloaded (or opened twice).
        var second = await _service.CompleteCheckoutAsync(payment.TransactionRef!, customer.Id);

        Assert.Equal(OnlinePaymentState.Paid, second.State);
        Assert.Equal(1, await _test.Db.Payments.CountAsync(p => p.BillId == bill.Id));
        Assert.Equal(firstPaidAt, (await ReadPaymentAsync(payment.Id)).PaidAt);
    }

    [Fact]
    public async Task PaidSessionWithWrongAmount_IsNotRecorded()
    {
        var (customer, bill) = await ConfirmedBillAsync();
        var payment = await PendingOnlinePaymentAsync(bill.Id);
        _stripe.Sessions[payment.TransactionRef!] =
            FakeStripeCheckoutGateway.Paid(payment.TransactionRef!, BillTotalInSen - 100, payment.Id);

        var outcome = await _service.CompleteCheckoutAsync(payment.TransactionRef!, customer.Id);

        Assert.Equal(OnlinePaymentState.StillPending, outcome.State);
        Assert.Equal(PaymentStatus.Pending, (await ReadPaymentAsync(payment.Id)).Status);
        Assert.Equal(BillStatus.Confirmed, (await ReadBillAsync(bill.Id)).Status);
    }

    [Fact]
    public async Task PaidSessionWithWrongCurrency_IsNotRecorded()
    {
        var (customer, bill) = await ConfirmedBillAsync();
        var payment = await PendingOnlinePaymentAsync(bill.Id);
        _stripe.Sessions[payment.TransactionRef!] =
            FakeStripeCheckoutGateway.Paid(payment.TransactionRef!, BillTotalInSen, payment.Id, currency: "usd");

        var outcome = await _service.CompleteCheckoutAsync(payment.TransactionRef!, customer.Id);

        Assert.Equal(OnlinePaymentState.StillPending, outcome.State);
        Assert.Equal(BillStatus.Confirmed, (await ReadBillAsync(bill.Id)).Status);
    }

    [Fact]
    public async Task UnfinishedPaymentOlderThan30Minutes_IsExpiredAndFailed()
    {
        var (customer, bill) = await ConfirmedBillAsync();
        var payment = await PendingOnlinePaymentAsync(bill.Id, DateTime.Now.AddMinutes(-31));
        _stripe.Sessions[payment.TransactionRef!] =
            FakeStripeCheckoutGateway.Open(payment.TransactionRef!, BillTotalInSen, payment.Id);

        var outcome = await _service.CompleteCheckoutAsync(payment.TransactionRef!, customer.Id);

        Assert.Equal(OnlinePaymentState.Failed, outcome.State);
        Assert.Contains(payment.TransactionRef!, _stripe.ExpiredSessionIds);
        Assert.Equal(PaymentStatus.Failed, (await ReadPaymentAsync(payment.Id)).Status);
        Assert.Equal(BillStatus.Confirmed, (await ReadBillAsync(bill.Id)).Status);
    }

    [Fact]
    public async Task UnfinishedPaymentWithin30Minutes_StaysPending()
    {
        var (customer, bill) = await ConfirmedBillAsync();
        var payment = await PendingOnlinePaymentAsync(bill.Id, DateTime.Now.AddMinutes(-5));
        _stripe.Sessions[payment.TransactionRef!] =
            FakeStripeCheckoutGateway.Open(payment.TransactionRef!, BillTotalInSen, payment.Id);

        var outcome = await _service.CompleteCheckoutAsync(payment.TransactionRef!, customer.Id);

        Assert.Equal(OnlinePaymentState.StillPending, outcome.State);
        Assert.Empty(_stripe.ExpiredSessionIds);
        Assert.Equal(PaymentStatus.Pending, (await ReadPaymentAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task AnotherCustomer_CannotOpenThePaymentResult()
    {
        var (_, bill) = await ConfirmedBillAsync();
        var stranger = await TestData.CreateUserAsync(_test.Db);
        var payment = await PendingOnlinePaymentAsync(bill.Id);
        _stripe.Sessions[payment.TransactionRef!] =
            FakeStripeCheckoutGateway.Paid(payment.TransactionRef!, BillTotalInSen, payment.Id);

        var outcome = await _service.CompleteCheckoutAsync(payment.TransactionRef!, stranger.Id);

        Assert.Equal(OnlinePaymentState.NotFound, outcome.State);
        Assert.Equal(PaymentStatus.Pending, (await ReadPaymentAsync(payment.Id)).Status);
    }

    public void Dispose() => _test.Dispose();
}
