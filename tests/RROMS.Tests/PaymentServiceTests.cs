
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services;

namespace RROMS.Tests;

public class PaymentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly PaymentService _service;
    private readonly Order _order;

    public PaymentServiceTests()
    {
        // Each test gets its own temporary SQLite database.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();

        // Create a test customer to satisfy Order.UserId FK.
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "us31-test@example.com",
            Email = "us31-test@example.com"
        };

        _db.Users.Add(user);

        _order = new Order
        {
            OrderNo = "TEST-" + Guid.NewGuid().ToString("N")[..12],
            UserId = user.Id,
            Status = OrderStatus.Pending,
            OrderType = OrderType.DineIn,
            TotalAmount = 50.00m,
            Subtotal = 50.00m
        };

        _db.Orders.Add(_order);
        _db.SaveChanges();

        var payments = new Repository<Payment>(_db);
        var orders = new Repository<Order>(_db);
        var bills = new Repository<Bill>(_db);

        // SQLite has no sp_getapplock, so use the fake that just runs the operation.
        _service = new PaymentService(
            payments, orders, bills,
            new FakeBillingTransactionRepository());
    }

    [Fact]
    public async Task ValidPayment_CreatesPending()
    {
        var payment = await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Cash, "TEST-001");

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(_order.Id, payment.OrderId);
        Assert.Equal(50.00m, payment.Amount);
        Assert.Null(payment.PaidAt);
    }

    [Fact]
    public async Task PendingPayment_CanBeConfirmed()
    {
        var payment = await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Cash);

        var result = await _service.ConfirmPaymentAsync(payment.Id);

        Assert.Equal(PaymentStatus.Paid, result.Status);
        Assert.NotNull(result.PaidAt);
    }

    [Fact]
    public async Task PendingPayment_CanBeMarkedFailed()
    {
        var payment = await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Card);

        var result = await _service.MarkPaymentFailedAsync(payment.Id);

        Assert.Equal(PaymentStatus.Failed, result.Status);
        Assert.Null(result.PaidAt);
    }

    [Fact]
    public async Task FailedPayment_AllowsRetry()
    {
        var first = await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Card, "FAILED-001");

        // MarkPaymentFailedAsync now uses a conditional database update
        // (US26 lock), so check the returned payment and the saved row
        // instead of the object returned by RecordPaymentAsync.
        var failed = await _service.MarkPaymentFailedAsync(first.Id);

        var retry = await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Cash, "RETRY-001");

        var savedFirst = await _db.Payments.AsNoTracking()
            .SingleAsync(p => p.Id == first.Id);

        Assert.Equal(PaymentStatus.Failed, failed.Status);
        Assert.Equal(PaymentStatus.Failed, savedFirst.Status);
        Assert.Equal(PaymentStatus.Pending, retry.Status);
        Assert.NotEqual(first.Id, retry.Id);
    }

    [Fact]
    public async Task DuplicatePendingPayment_IsRejected()
    {
        await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Cash);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordPaymentAsync(
                _order.Id, 50.00m, PaymentMethod.Card));
    }

    [Fact]
    public async Task DuplicatePaidPayment_IsRejected()
    {
        var payment = await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Cash);

        await _service.ConfirmPaymentAsync(payment.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordPaymentAsync(
                _order.Id, 50.00m, PaymentMethod.Card));
    }

    [Fact]
    public async Task DuplicateTransactionReference_IsRejected()
    {
        var first = await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Card, "SAME-REF");

        await _service.MarkPaymentFailedAsync(first.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordPaymentAsync(
                _order.Id, 50.00m, PaymentMethod.Card, "SAME-REF"));
    }

    [Fact]
    public async Task InvalidAmount_IsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordPaymentAsync(
                _order.Id, 40.00m, PaymentMethod.Cash));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.RecordPaymentAsync(
                _order.Id, 0m, PaymentMethod.Cash));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.RecordPaymentAsync(
                _order.Id, 50.001m, PaymentMethod.Cash));
    }

    [Fact]
    public async Task PaidPayment_CannotBeMarkedFailed()
    {
        var payment = await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Cash);

        await _service.ConfirmPaymentAsync(payment.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.MarkPaymentFailedAsync(payment.Id));
    }

    [Fact]
    public async Task FailedPayment_CannotBeConfirmed()
    {
        var payment = await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Card);

        await _service.MarkPaymentFailedAsync(payment.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ConfirmPaymentAsync(payment.Id));
    }

    [Fact]
    public async Task PaymentHistory_ReturnsTransactions()
    {
        var first = await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Card);

        await _service.MarkPaymentFailedAsync(first.Id);

        var second = await _service.RecordPaymentAsync(
            _order.Id, 50.00m, PaymentMethod.Cash);

        var history = await _service.GetPaymentsByOrderIdAsync(_order.Id);

        Assert.Equal(2, history.Count);
        Assert.Contains(history, p => p.Id == first.Id);
        Assert.Contains(history, p => p.Id == second.Id);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
