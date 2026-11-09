using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Tests;

// QA01 (Member 4): US32 combined billing.
// Runs on in-memory SQLite. SQLite has no sp_getapplock, so the fake
// repository runs each operation without the database lock; the lock
// itself is tested on SQL Server in BillingConcurrencySqlServerTests.
public class BillingServiceTests : IDisposable
{
    private readonly SqliteTestDb _test = new();
    private readonly BillingService _billing;

    public BillingServiceTests()
    {
        var db = _test.Db;

        _billing = new BillingService(
            new Repository<Order>(db),
            new Repository<Payment>(db),
            new Repository<Bill>(db),
            new Repository<DiningSession>(db),
            new FakeBillingTransactionRepository());
    }

    private async Task<(ApplicationUser Customer, MenuItem Item, DiningSession Session)> NewTableAsync()
    {
        var db = _test.Db;
        var customer = await TestData.CreateUserAsync(db);
        var item = await TestData.CreateMenuItemAsync(db);
        var session = await TestData.CreateOpenSessionAsync(db, customer.Id);
        return (customer, item, session);
    }

    [Fact]
    public async Task SessionBill_TotalsEqualSumOfSavedOrderAmounts()
    {
        var (customer, item, session) = await NewTableAsync();

        // Two ordering rounds with deliberately uneven saved charges:
        // the bill must add them up, never recalculate them.
        var round1 = await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            40.00m, 2.64m, 4.00m, sessionId: session.Id);
        var round2 = await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            20.00m, 1.32m, 2.00m, sessionId: session.Id);

        var preview = await _billing.PreviewSessionBillAsync(session.Id);
        var result = await _billing.ConfirmSessionBillAsync(
            session.Id, "staff-1", preview.OrderSnapshot, acknowledgeKitchenWarning: false);

        Assert.Equal(SessionBillConfirmStatus.Confirmed, result.Status);

        var bill = await _test.Db.Bills.AsNoTracking().SingleAsync(b => b.DiningSessionId == session.Id);

        Assert.Equal(60.00m, bill.Subtotal);
        Assert.Equal(3.96m, bill.TaxAmount);
        Assert.Equal(6.00m, bill.ServiceCharge);
        Assert.Equal(round1.TotalAmount + round2.TotalAmount, bill.TotalAmount);
        Assert.Equal(BillStatus.Confirmed, bill.Status);
        Assert.Equal("staff-1", bill.ConfirmedByUserId);
        Assert.NotNull(bill.ConfirmedAt);

        var linked = await _test.Db.Orders.AsNoTracking()
            .Where(o => o.DiningSessionId == session.Id)
            .Select(o => o.BillId)
            .ToListAsync();

        Assert.All(linked, id => Assert.Equal(bill.Id, id));
    }

    [Fact]
    public async Task SessionBill_CancelledOrderIsLeftOut()
    {
        var (customer, item, session) = await NewTableAsync();

        var served = await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            30.00m, 1.80m, 3.00m, sessionId: session.Id);
        var cancelled = await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            15.00m, 0.90m, 1.50m, status: OrderStatus.Cancelled, sessionId: session.Id);

        var preview = await _billing.PreviewSessionBillAsync(session.Id);
        await _billing.ConfirmSessionBillAsync(session.Id, "staff-1", preview.OrderSnapshot, false);

        var bill = await _test.Db.Bills.AsNoTracking().SingleAsync(b => b.DiningSessionId == session.Id);
        var cancelledNow = await _test.Db.Orders.AsNoTracking().SingleAsync(o => o.Id == cancelled.Id);

        Assert.Single(preview.Orders);
        Assert.Equal(served.TotalAmount, bill.TotalAmount);
        Assert.Null(cancelledNow.BillId);
    }

    [Fact]
    public async Task SessionBill_OrdersStillInKitchen_NeedStaffAcknowledgement()
    {
        var (customer, item, session) = await NewTableAsync();

        await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            25.00m, 1.50m, 2.50m, status: OrderStatus.Preparing, sessionId: session.Id);

        var preview = await _billing.PreviewSessionBillAsync(session.Id);

        var warned = await _billing.ConfirmSessionBillAsync(
            session.Id, "staff-1", preview.OrderSnapshot, acknowledgeKitchenWarning: false);

        Assert.Equal(SessionBillConfirmStatus.KitchenWarningRequired, warned.Status);
        Assert.Null(warned.Bill);
        Assert.Single(warned.OrdersInKitchen);
        Assert.False(await _test.Db.Bills.AnyAsync(b => b.DiningSessionId == session.Id));

        var confirmed = await _billing.ConfirmSessionBillAsync(
            session.Id, "staff-1", preview.OrderSnapshot, acknowledgeKitchenWarning: true);

        Assert.Equal(SessionBillConfirmStatus.Confirmed, confirmed.Status);
    }

    [Fact]
    public async Task SessionBill_CannotBeConfirmedTwice()
    {
        var (customer, item, session) = await NewTableAsync();

        await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            25.00m, 1.50m, 2.50m, sessionId: session.Id);

        var preview = await _billing.PreviewSessionBillAsync(session.Id);
        await _billing.ConfirmSessionBillAsync(session.Id, "staff-1", preview.OrderSnapshot, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _billing.ConfirmSessionBillAsync(session.Id, "staff-2", preview.OrderSnapshot, false));

        Assert.Equal(1, await _test.Db.Bills.CountAsync(b => b.DiningSessionId == session.Id));
    }

    [Fact]
    public async Task SessionBill_OrdersChangedSincePreview_IsRefused()
    {
        var (customer, item, session) = await NewTableAsync();

        await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            25.00m, 1.50m, 2.50m, sessionId: session.Id);

        var preview = await _billing.PreviewSessionBillAsync(session.Id);

        // A new ordering round arrives after staff opened the preview.
        await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            12.00m, 0.72m, 1.20m, sessionId: session.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _billing.ConfirmSessionBillAsync(session.Id, "staff-1", preview.OrderSnapshot, false));

        Assert.False(await _test.Db.Bills.AnyAsync(b => b.DiningSessionId == session.Id));
    }

    [Fact]
    public async Task IsSessionLocked_TrueOnlyAfterBillConfirmed()
    {
        var (customer, item, session) = await NewTableAsync();

        await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            25.00m, 1.50m, 2.50m, sessionId: session.Id);

        Assert.False(await _billing.IsSessionLockedAsync(session.Id));

        var preview = await _billing.PreviewSessionBillAsync(session.Id);
        await _billing.ConfirmSessionBillAsync(session.Id, "staff-1", preview.OrderSnapshot, false);

        Assert.True(await _billing.IsSessionLockedAsync(session.Id));
    }

    [Fact]
    public async Task IsSessionFinanciallySettled_FalseUntilBillIsPaid()
    {
        var (customer, item, session) = await NewTableAsync();

        await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            25.00m, 1.50m, 2.50m, sessionId: session.Id);

        // Unbilled order: not settled.
        Assert.False(await _billing.IsSessionFinanciallySettledAsync(session.Id));

        var preview = await _billing.PreviewSessionBillAsync(session.Id);
        var result = await _billing.ConfirmSessionBillAsync(
            session.Id, "staff-1", preview.OrderSnapshot, false);

        // Confirmed but unpaid: still not settled.
        Assert.False(await _billing.IsSessionFinanciallySettledAsync(session.Id));

        var payments = new PaymentService(
            new Repository<Payment>(_test.Db),
            new Repository<Order>(_test.Db),
            new Repository<Bill>(_test.Db),
            new FakeBillingTransactionRepository());

        await payments.RecordCounterPaymentAsync(result.Bill!.Id, PaymentMethod.Cash, "staff-1");

        Assert.True(await _billing.IsSessionFinanciallySettledAsync(session.Id));
    }

    [Fact]
    public async Task TakeawayBill_CopiesSavedAmounts_AndIsCreatedOnlyOnce()
    {
        var customer = await TestData.CreateUserAsync(_test.Db);
        var item = await TestData.CreateMenuItemAsync(_test.Db);

        var order = await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            18.00m, 1.08m, 0.00m, type: OrderType.Takeaway, status: OrderStatus.Pending);

        var first = await _billing.CreateTakeawayBillAsync(order.Id);
        var second = await _billing.CreateTakeawayBillAsync(order.Id);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(BillStatus.Draft, first.Status);
        Assert.Equal(order.Subtotal, first.Subtotal);
        Assert.Equal(order.TaxAmount, first.TaxAmount);
        Assert.Equal(order.TotalAmount, first.TotalAmount);
        Assert.Equal(1, await _test.Db.Bills.CountAsync());
    }

    [Fact]
    public async Task TakeawayBill_CannotBeConfirmedTwice()
    {
        var customer = await TestData.CreateUserAsync(_test.Db);
        var item = await TestData.CreateMenuItemAsync(_test.Db);

        var order = await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            18.00m, 1.08m, 0.00m, type: OrderType.Takeaway, status: OrderStatus.Pending);

        var bill = await _billing.CreateTakeawayBillAsync(order.Id);

        var confirmed = await _billing.ConfirmBillAsync(bill.Id, "staff-1");
        Assert.Equal(BillStatus.Confirmed, confirmed.Status);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _billing.ConfirmBillAsync(bill.Id, "staff-2"));

        var saved = await _test.Db.Bills.AsNoTracking().SingleAsync(b => b.Id == bill.Id);
        Assert.Equal("staff-1", saved.ConfirmedByUserId);
    }

    [Fact]
    public async Task TakeawayBill_CancelledOrder_IsRefused()
    {
        var customer = await TestData.CreateUserAsync(_test.Db);
        var item = await TestData.CreateMenuItemAsync(_test.Db);

        var order = await TestData.CreateOrderAsync(_test.Db, customer.Id, item,
            18.00m, 1.08m, 0.00m, type: OrderType.Takeaway, status: OrderStatus.Cancelled);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _billing.CreateTakeawayBillAsync(order.Id));

        Assert.False(await _test.Db.Bills.AnyAsync());
    }

    public void Dispose() => _test.Dispose();
}
