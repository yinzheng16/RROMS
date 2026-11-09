using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Tests;

// QA01 (Member 4): two staff members act on the same bill at the same time.
// Uses the REAL BillingTransactionRepository (SQL Server sp_getapplock) on a
// dedicated LocalDB test database, which is recreated once per test run.
// Each request has its own DbContext and connection, like two web requests.
public sealed class SqlServerBillingDatabase
{
    public const string DatabaseName = "RROMS_QA01_Test";

    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=RROMS_QA01_Test;Trusted_Connection=True;TrustServerCertificate=True;";

    public SqlServerBillingDatabase()
    {
        using var db = CreateContext();

        // Safety: only ever drop the dedicated test database.
        if (!string.Equals(db.Database.GetDbConnection().Database, DatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsafe database connection. Test cancelled.");
        }

        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    public ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);
}

public class BillingConcurrencySqlServerTests : IClassFixture<SqlServerBillingDatabase>
{
    private readonly SqlServerBillingDatabase _database;

    public BillingConcurrencySqlServerTests(SqlServerBillingDatabase database)
    {
        _database = database;
    }

    // Runs both requests together and returns the error of each (null = succeeded).
    private static async Task<Exception?[]> RunTogetherAsync(params Func<Task>[] requests)
    {
        using var start = new SemaphoreSlim(0);

        var tasks = requests.Select(request => Task.Run(async () =>
        {
            await start.WaitAsync();
            try
            {
                await request();
                return (Exception?)null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        })).ToArray();

        start.Release(requests.Length);
        return await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task TwoStaffPayingTheSameBill_OnlyOnePaymentIsRecorded()
    {
        int billId;

        await using (var setup = _database.CreateContext())
        {
            var customer = await TestData.CreateUserAsync(setup);
            var item = await TestData.CreateMenuItemAsync(setup);
            var (bill, _) = await TestData.CreateTakeawayBillAsync(
                setup, customer.Id, item, 30.00m, BillStatus.Confirmed);
            billId = bill.Id;
        }

        Func<Task> Pay(string staffId, PaymentMethod method) => async () =>
        {
            await using var db = _database.CreateContext();
            var service = new PaymentService(
                new Repository<Payment>(db),
                new Repository<Order>(db),
                new Repository<Bill>(db),
                new BillingTransactionRepository(db));

            await service.RecordCounterPaymentAsync(billId, method, staffId);
        };

        var errors = await RunTogetherAsync(
            Pay("staff-A", PaymentMethod.Cash),
            Pay("staff-B", PaymentMethod.Card));

        await using var check = _database.CreateContext();
        var payments = await check.Payments.AsNoTracking()
            .Where(p => p.BillId == billId)
            .ToListAsync();
        var savedBill = await check.Bills.AsNoTracking().SingleAsync(b => b.Id == billId);

        Assert.Equal(1, errors.Count(e => e == null));
        Assert.IsType<InvalidOperationException>(Assert.Single(errors, e => e != null));
        Assert.Single(payments);
        Assert.Equal(PaymentStatus.Paid, payments[0].Status);
        Assert.Equal(BillStatus.Paid, savedBill.Status);
    }

    [Fact]
    public async Task TwoStaffConfirmingTheSameTable_OnlyOneBillIsCreated()
    {
        int sessionId;
        string snapshot;

        await using (var setup = _database.CreateContext())
        {
            var customer = await TestData.CreateUserAsync(setup);
            var item = await TestData.CreateMenuItemAsync(setup);
            var session = await TestData.CreateOpenSessionAsync(setup, customer.Id);

            await TestData.CreateOrderAsync(setup, customer.Id, item,
                40.00m, 2.64m, 4.00m, sessionId: session.Id);
            await TestData.CreateOrderAsync(setup, customer.Id, item,
                20.00m, 1.32m, 2.00m, sessionId: session.Id);

            sessionId = session.Id;
            snapshot = (await CreateBillingService(setup).PreviewSessionBillAsync(sessionId)).OrderSnapshot;
        }

        Func<Task> Confirm(string staffId) => async () =>
        {
            await using var db = _database.CreateContext();
            var result = await CreateBillingService(db)
                .ConfirmSessionBillAsync(sessionId, staffId, snapshot, acknowledgeKitchenWarning: false);

            if (result.Status != SessionBillConfirmStatus.Confirmed)
                throw new InvalidOperationException("Bill was not confirmed.");
        };

        var errors = await RunTogetherAsync(Confirm("staff-A"), Confirm("staff-B"));

        await using var check = _database.CreateContext();
        var bills = await check.Bills.AsNoTracking()
            .Where(b => b.DiningSessionId == sessionId)
            .ToListAsync();
        var orderBillIds = await check.Orders.AsNoTracking()
            .Where(o => o.DiningSessionId == sessionId)
            .Select(o => o.BillId)
            .ToListAsync();

        Assert.Equal(1, errors.Count(e => e == null));
        Assert.IsType<InvalidOperationException>(Assert.Single(errors, e => e != null));
        var bill = Assert.Single(bills);
        Assert.Equal(69.96m, bill.TotalAmount);
        Assert.All(orderBillIds, id => Assert.Equal(bill.Id, id));
    }

    private static BillingService CreateBillingService(ApplicationDbContext db) =>
        new(new Repository<Order>(db),
            new Repository<Payment>(db),
            new Repository<Bill>(db),
            new Repository<DiningSession>(db),
            new BillingTransactionRepository(db));
}
