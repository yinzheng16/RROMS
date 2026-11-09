using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;

namespace RROMS.Tests;

// QA01 (Member 4): shared helpers for the module tests.
// SqliteTestDb gives each test class its own temporary in-memory SQLite database.
// TestData creates the rows the billing tests need (customer, menu item,
// table, dining session, orders and bills) with unique names, so the same
// helpers also work on the SQL Server test database.
public sealed class SqliteTestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public ApplicationDbContext Db { get; }

    public SqliteTestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new ApplicationDbContext(_options);
        Db.Database.EnsureCreated();
    }

    // A second context on the same database (e.g. for a background worker).
    public ApplicationDbContext NewContext() => new(_options);

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

public static class TestData
{
    private static string Unique(int length) =>
        Guid.NewGuid().ToString("N")[..length];

    public static async Task<ApplicationUser> CreateUserAsync(ApplicationDbContext db)
    {
        string suffix = Unique(12);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            FullName = "Test Customer",
            UserName = $"qa01-{suffix}@example.com",
            NormalizedUserName = $"QA01-{suffix}@EXAMPLE.COM",
            Email = $"qa01-{suffix}@example.com",
            NormalizedEmail = $"QA01-{suffix}@EXAMPLE.COM"
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<MenuItem> CreateMenuItemAsync(ApplicationDbContext db)
    {
        var category = new MenuCategory
        {
            Name = "QA01 " + Unique(10),
            Code = "Q" + Unique(10)
        };

        var item = new MenuItem
        {
            Category = category,
            Name = "QA01 dish " + Unique(8),
            Code = "QA" + Unique(12),
            Price = 10.00m,
            IsAvailable = true
        };

        db.MenuItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    // An open dining session at a new table, for the given customer.
    public static async Task<DiningSession> CreateOpenSessionAsync(
        ApplicationDbContext db, string customerId)
    {
        var area = new DiningArea { Name = "QA01 area " + Unique(8) };

        var table = new RestaurantTable
        {
            DiningArea = area,
            TableNumber = "Q" + Unique(8),
            Capacity = 4
        };

        var session = new DiningSession
        {
            Table = table,
            CustomerId = customerId,
            Status = DiningSessionStatus.Open,
            OpenedAt = DateTime.Now
        };

        db.DiningSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    // One order with one line. TotalAmount = subtotal + tax + service,
    // exactly as Member 3's checkout saves it.
    public static async Task<Order> CreateOrderAsync(
        ApplicationDbContext db,
        string userId,
        MenuItem item,
        decimal subtotal,
        decimal tax,
        decimal service,
        OrderType type = OrderType.DineIn,
        OrderStatus status = OrderStatus.Served,
        int? sessionId = null,
        int? billId = null,
        decimal? taxRate = null,
        decimal? serviceRate = null)
    {
        var order = new Order
        {
            OrderNo = "T" + Unique(12),
            UserId = userId,
            OrderType = type,
            Status = status,
            DiningSessionId = sessionId,
            BillId = billId,
            Subtotal = subtotal,
            TaxAmount = tax,
            ServiceCharge = service,
            TotalAmount = subtotal + tax + service,
            TaxRatePercent = taxRate,
            ServiceChargePercent = serviceRate,
            CreatedAt = DateTime.Now
        };

        order.Items.Add(new OrderItem
        {
            MenuItemId = item.Id,
            Quantity = 1,
            UnitPrice = subtotal
        });

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    // A takeaway order with its own bill in the given status.
    public static async Task<(Bill Bill, Order Order)> CreateTakeawayBillAsync(
        ApplicationDbContext db,
        string customerId,
        MenuItem item,
        decimal total,
        BillStatus status,
        string? confirmedBy = "staff-1")
    {
        var bill = new Bill
        {
            DiningSessionId = null,
            Subtotal = total,
            TaxAmount = 0m,
            ServiceCharge = 0m,
            TotalAmount = total,
            Status = status,
            ConfirmedAt = status == BillStatus.Draft ? null : DateTime.Now,
            ConfirmedByUserId = status == BillStatus.Draft ? null : confirmedBy
        };

        db.Bills.Add(bill);
        await db.SaveChangesAsync();

        var order = await CreateOrderAsync(
            db, customerId, item, total, 0m, 0m,
            type: OrderType.Takeaway,
            status: OrderStatus.Completed,
            billId: bill.Id);

        return (bill, order);
    }

    public static async Task<Payment> CreatePaymentAsync(
        ApplicationDbContext db,
        int billId,
        decimal amount,
        PaymentMethod method,
        PaymentStatus status,
        string? transactionRef = null,
        DateTime? createdAt = null)
    {
        DateTime created = createdAt ?? DateTime.Now;

        var payment = new Payment
        {
            BillId = billId,
            OrderId = null,
            Amount = amount,
            Method = method,
            Status = status,
            TransactionRef = transactionRef,
            CreatedAt = created,
            PaidAt = status == PaymentStatus.Paid ? created : null
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }
}
