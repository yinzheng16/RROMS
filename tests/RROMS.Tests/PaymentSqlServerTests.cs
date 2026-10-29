
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RROMS.Data;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services;

namespace RROMS.Tests;

public class PaymentSqlServerTests
{
    private const string TestDatabaseName = "RROMS_US31_Test";

    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=RROMS_US31_Test;Trusted_Connection=True;TrustServerCertificate=True;";

    // --------------------------------------------------
    // Helper: Connect only to the dedicated test database
    // --------------------------------------------------
    private static async Task<ApplicationDbContext> CreateTestContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        var db = new ApplicationDbContext(options);

        try
        {
            var connection = db.Database.GetDbConnection();

            if (!string.Equals(
                    connection.Database,
                    TestDatabaseName,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    connection.DataSource,
                    @"(localdb)\MSSQLLocalDB",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Unsafe database connection. Test cancelled.");
            }

            if (!await db.Database.CanConnectAsync())
            {
                throw new InvalidOperationException(
                    "Cannot connect to the test database.");
            }

            var actualDatabase = await db.Database
                .SqlQueryRaw<string>("SELECT DB_NAME() AS [Value]")
                .SingleAsync();

            if (!string.Equals(
                    actualDatabase,
                    TestDatabaseName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Connected to an unexpected database.");
            }

            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    // --------------------------------------------------
    // Helper: Create a test order
    // --------------------------------------------------
    private static async Task<Order> CreateTestOrderAsync(
        ApplicationDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"us31-{suffix}@example.com",
            NormalizedUserName = $"US31-{suffix}@EXAMPLE.COM",
            Email = $"us31-{suffix}@example.com",
            NormalizedEmail = $"US31-{suffix}@EXAMPLE.COM"
        };

        db.Users.Add(user);

        var order = new Order
        {
            OrderNo = $"T-{suffix}",
            UserId = user.Id,
            OrderType = OrderType.DineIn,
            Status = OrderStatus.Pending,
            Subtotal = 50.00m,
            TaxAmount = 0.00m,
            ServiceCharge = 0.00m,
            TotalAmount = 50.00m,
            CreatedAt = DateTime.UtcNow
        };

        db.Orders.Add(order);

        await db.SaveChangesAsync();

        return order;
    }

    // --------------------------------------------------
    // Helper: Create the real PaymentService
    // --------------------------------------------------
    private static PaymentService CreateService(
        ApplicationDbContext db)
    {
        // The fake runs the operation directly. The real repository
        // would start its own transaction, but each test already runs
        // inside a transaction that is rolled back at the end.
        return new PaymentService(
            new Repository<Payment>(db),
            new Repository<Order>(db),
            new Repository<Bill>(db),
            new FakeBillingTransactionRepository());
    }

    // ==================================================
    // TEST 1: Database schema
    // ==================================================
    [Fact]
    public async Task TestDatabaseSchema_CanBeCreated()
    {
        await using var db = await CreateTestContextAsync();

        // Do not delete or recreate an existing database.
        // EnsureCreated only initializes an empty database.
        await db.Database.EnsureCreatedAsync();

        var tableNames = await db.Database
            .SqlQueryRaw<string>(
                "SELECT TABLE_NAME AS [Value] " +
                "FROM INFORMATION_SCHEMA.TABLES " +
                "WHERE TABLE_TYPE = 'BASE TABLE'")
            .ToListAsync();

        Assert.Contains("AspNetUsers", tableNames);
        Assert.Contains("Orders", tableNames);
        Assert.Contains("Payments", tableNames);
        Assert.Contains("Reservations", tableNames);
    }

    // ==================================================
    // TEST 2: Valid payment is saved to SQL Server
    // ==================================================
    [Fact]
    public async Task SqlServer_ValidPayment_IsPersisted()
    {
        await using var db = await CreateTestContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var order = await CreateTestOrderAsync(db);
        var service = CreateService(db);

        var payment = await service.RecordPaymentAsync(
            order.Id,
            50.00m,
            PaymentMethod.Cash);

        db.ChangeTracker.Clear();

        var saved = await db.Payments
            .AsNoTracking()
            .SingleAsync(p => p.Id == payment.Id);

        Assert.Equal(order.Id, saved.OrderId);
        Assert.Equal(50.00m, saved.Amount);
        Assert.Equal(PaymentMethod.Cash, saved.Method);
        Assert.Equal(PaymentStatus.Pending, saved.Status);
        Assert.Null(saved.PaidAt);

        await tx.RollbackAsync();
    }

    // ==================================================
    // TEST 3: Invalid OrderId violates SQL Server FK
    // ==================================================
    [Fact]
    public async Task SqlServer_InvalidOrderId_ForeignKeyRejectsPayment()
    {
        await using var db = await CreateTestContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        // Bypass PaymentService to test SQL Server itself.
        db.Payments.Add(new Payment
        {
            OrderId = -999999,
            Amount = 50.00m,
            Method = PaymentMethod.Cash,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(
            () => db.SaveChangesAsync());

        await tx.RollbackAsync();
    }

    // ==================================================
    // TEST 4: Pending -> Paid persists correctly
    // ==================================================
    [Fact]
    public async Task SqlServer_PaymentStatusChanges_ArePersisted()
    {
        await using var db = await CreateTestContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var order = await CreateTestOrderAsync(db);
        var service = CreateService(db);

        var payment = await service.RecordPaymentAsync(
            order.Id,
            50.00m,
            PaymentMethod.Cash);

        await service.ConfirmPaymentAsync(payment.Id);

        db.ChangeTracker.Clear();

        var saved = await db.Payments
            .AsNoTracking()
            .SingleAsync(p => p.Id == payment.Id);

        Assert.Equal(PaymentStatus.Paid, saved.Status);
        Assert.NotNull(saved.PaidAt);

        // Verify that SQL Server stores the enum as text.
        var rawStatus = await db.Database
            .SqlQueryRaw<string>(
                "SELECT [Status] AS [Value] " +
                "FROM [Payments] WHERE [Id] = {0}",
                payment.Id)
            .SingleAsync();

        Assert.Equal("Paid", rawStatus);

        await tx.RollbackAsync();
    }

    // ==================================================
    // TEST 5: Failed payment can be retried
    // ==================================================
    [Fact]
    public async Task SqlServer_FailedPayment_CanBeRetried()
    {
        await using var db = await CreateTestContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var order = await CreateTestOrderAsync(db);
        var service = CreateService(db);

        var firstPayment = await service.RecordPaymentAsync(
            order.Id,
            50.00m,
            PaymentMethod.Card);

        await service.MarkPaymentFailedAsync(firstPayment.Id);

        var secondPayment = await service.RecordPaymentAsync(
            order.Id,
            50.00m,
            PaymentMethod.Cash);

        db.ChangeTracker.Clear();

        var history = await db.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == order.Id)
            .ToListAsync();

        Assert.Equal(2, history.Count);

        Assert.Contains(history, p =>
            p.Id == firstPayment.Id &&
            p.Status == PaymentStatus.Failed);

        Assert.Contains(history, p =>
            p.Id == secondPayment.Id &&
            p.Status == PaymentStatus.Pending);

        await tx.RollbackAsync();
    }

    // ==================================================
    // TEST 6: Duplicate active payment is rejected
    // ==================================================
    [Fact]
    public async Task SqlServer_DuplicatePayment_IsRejected()
    {
        await using var db = await CreateTestContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var order = await CreateTestOrderAsync(db);
        var service = CreateService(db);

        await service.RecordPaymentAsync(
            order.Id,
            50.00m,
            PaymentMethod.Cash);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordPaymentAsync(
                order.Id,
                50.00m,
                PaymentMethod.Card));

        var paymentCount = await db.Payments
            .CountAsync(p => p.OrderId == order.Id);

        Assert.Equal(1, paymentCount);

        await tx.RollbackAsync();
    }


    // ==================================================
    // TEST 7: Duplicate transaction reference is rejected
    // ==================================================
    [Fact]
    public async Task SqlServer_DuplicateTransactionReference_IsRejected()
    {
        await using var db = await CreateTestContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var firstOrder = await CreateTestOrderAsync(db);
        var secondOrder = await CreateTestOrderAsync(db);
        var service = CreateService(db);

        var reference = "US31-" + Guid.NewGuid().ToString("N");

        var firstPayment = await service.RecordPaymentAsync(
            firstOrder.Id,
            50.00m,
            PaymentMethod.Card,
            reference);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordPaymentAsync(
                secondOrder.Id,
                50.00m,
                PaymentMethod.Card,
                reference));

        var savedPayments = await db.Payments
            .AsNoTracking()
            .Where(p => p.TransactionRef == reference)
            .ToListAsync();

        Assert.Single(savedPayments);
        Assert.Equal(firstPayment.Id, savedPayments[0].Id);

        await tx.RollbackAsync();
    }

    // ==================================================
    // TEST 8: Paid payment cannot transition to Failed
    // ==================================================
    [Fact]
    public async Task SqlServer_PaidToFailed_IsRejected()
    {
        await using var db = await CreateTestContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var order = await CreateTestOrderAsync(db);
        var service = CreateService(db);

        var payment = await service.RecordPaymentAsync(
            order.Id,
            50.00m,
            PaymentMethod.Cash);

        await service.ConfirmPaymentAsync(payment.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MarkPaymentFailedAsync(payment.Id));

        db.ChangeTracker.Clear();

        var saved = await db.Payments
            .AsNoTracking()
            .SingleAsync(p => p.Id == payment.Id);

        Assert.Equal(PaymentStatus.Paid, saved.Status);
        Assert.NotNull(saved.PaidAt);

        await tx.RollbackAsync();
    }

    // ==================================================
    // TEST 9: Payment history returns correct records
    // ==================================================
    [Fact]
    public async Task SqlServer_PaymentHistory_ReturnsNewestFirst()
    {
        await using var db = await CreateTestContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var order = await CreateTestOrderAsync(db);
        var service = CreateService(db);

        var firstPayment = await service.RecordPaymentAsync(
            order.Id,
            50.00m,
            PaymentMethod.Card);

        await service.MarkPaymentFailedAsync(firstPayment.Id);

        var secondPayment = await service.RecordPaymentAsync(
            order.Id,
            50.00m,
            PaymentMethod.Cash);

        // Set distinct timestamps to verify the ordering.
        firstPayment.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
        secondPayment.CreatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        var history = await service.GetPaymentsByOrderIdAsync(order.Id);

        Assert.Equal(2, history.Count);

        Assert.Equal(secondPayment.Id, history[0].Id);
        Assert.Equal(PaymentStatus.Pending, history[0].Status);

        Assert.Equal(firstPayment.Id, history[1].Id);
        Assert.Equal(PaymentStatus.Failed, history[1].Status);

        await tx.RollbackAsync();
    }

}
