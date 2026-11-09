// Owner: Member 3 (Tan Khai Wern). Menu, Orders and Recommendations (US21-US30).
// UNIT TESTS (UT-M3-xx): pure business rules, no database.
// INTEGRATION TESTS (IT-M3-xx): the real CustomerCartService / OrderService working with the real
// ApplicationDbContext schema on a temporary in-memory SQLite database (Member 1's TestHost).
// Other members' services (reservations, notifications, profiles) are replaced by recording stubs.
// Test IDs match Chapter 6 of the Member 3 individual report.
using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services;
using RROMS.Services.Interfaces;
using RROMS.Tests.Member1;

namespace RROMS.Tests.Member3;

// ======================= UNIT TESTS =======================

// ---------- US25 / US26: kitchen status workflow (OrderStatusRules) ----------
public class OrderStatusRulesTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Preparing)]
    [InlineData(OrderStatus.Preparing, OrderStatus.ReadyToServe)]
    [InlineData(OrderStatus.ReadyToServe, OrderStatus.Served)]
    [InlineData(OrderStatus.Served, OrderStatus.Completed)]
    public void UT_M3_01_EachStatus_MovesOnlyToTheNextStep(OrderStatus from, OrderStatus expectedNext)
    {
        Assert.Equal(expectedNext, OrderStatusRules.Next(from));
        Assert.Null(OrderStatusRules.Validate(from, expectedNext, null));
    }

    [Fact]
    public void UT_M3_02_SkippingAStep_IsRejected()
    {
        Assert.NotNull(OrderStatusRules.Validate(OrderStatus.Pending, OrderStatus.Served, null));
        Assert.Null(OrderStatusRules.Next(OrderStatus.Completed));
    }

    [Fact]
    public void UT_M3_03_CancellingAPreparingOrder_NeedsAReason()
    {
        Assert.NotNull(OrderStatusRules.Validate(OrderStatus.Preparing, OrderStatus.Cancelled, "  "));
        Assert.Null(OrderStatusRules.Validate(OrderStatus.Preparing, OrderStatus.Cancelled, "Customer left"));
        Assert.Null(OrderStatusRules.Validate(OrderStatus.Pending, OrderStatus.Cancelled, null));
    }

    [Fact]
    public void UT_M3_04_ServedOrCompletedOrders_CannotBeCancelled_AndReasonIsLimited()
    {
        Assert.False(OrderStatusRules.CanCancel(OrderStatus.Served));
        Assert.False(OrderStatusRules.CanCancel(OrderStatus.Completed));
        Assert.NotNull(OrderStatusRules.Validate(OrderStatus.Pending, OrderStatus.Cancelled, new string('x', 251)));
    }
}

// ---------- US29: promotional prices (PromotionRules) ----------
public class PromotionRulesTests
{
    [Fact]
    public void UT_M3_05_PercentAndFixedPromotions_GiveTheRightPrice()
    {
        Assert.Equal(20.00m, PromotionRules.PriceUnder(25.00m, 20m, null));   // 20% off RM25
        Assert.Equal(18.00m, PromotionRules.PriceUnder(25.00m, 0m, 18.00m));  // fixed price RM18
        Assert.Equal(25.00m, PromotionRules.PriceUnder(25.00m, 0m, 30.00m));  // never above the normal price
    }

    [Fact]
    public void UT_M3_06_OnlyActivePromotionsInsideTheirDates_AreRunning()
    {
        var now = new DateTime(2026, 11, 5, 12, 0, 0);
        var promotions = new List<Promotion>
        {
            new() { Id = 1, MenuItemId = 1, IsActive = true,  StartDate = now.AddDays(-1), EndDate = now.AddDays(2) },  // running
            new() { Id = 2, MenuItemId = 1, IsActive = true,  StartDate = now.AddDays(-9), EndDate = now.AddDays(-1) }, // ended
            new() { Id = 3, MenuItemId = 1, IsActive = true,  StartDate = now.AddDays(1),  EndDate = now.AddDays(5) },  // not started
            new() { Id = 4, MenuItemId = 1, IsActive = false, StartDate = now.AddDays(-1), EndDate = now.AddDays(2) },  // switched off
        };

        var running = PromotionRules.Running(promotions.AsQueryable(), now).Select(p => p.Id).ToList();

        Assert.Equal(new[] { 1 }, running);
    }
}

// ---------- US21 / US22: menu codes (MenuCodeRules) ----------
public class MenuCodeRulesTests
{
    [Fact]
    public void UT_M3_07_KnownCategory_GetsItsCode_AndATakenCodeIsNotReused()
    {
        Assert.Equal("SS", MenuCodeRules.CategoryCode("Sashimi", Array.Empty<string>()));
        Assert.Equal("SH", MenuCodeRules.CategoryCode("Sashimi", new[] { "SS" }));
    }

    [Fact]
    public void UT_M3_08_DishCodes_CountUpWithinTheCategory()
    {
        Assert.Equal(("SS01", 1), MenuCodeRules.NextItemCode("SS", 0));
        Assert.Equal(("SS10", 10), MenuCodeRules.NextItemCode("SS", 9));
    }
}

// ---------- US28: reorder choices (CustomerOrderHistoryRules) ----------
public class ReorderRulesTests
{
    [Fact]
    public void UT_M3_09_InvalidReorderSelections_AreRejected()
    {
        Assert.NotNull(CustomerOrderHistoryRules.ValidateChoices(new List<HistoryReorderChoice>
            { new() { OrderItemId = 1, Selected = false, Quantity = 1 } }));                       // nothing selected
        Assert.NotNull(CustomerOrderHistoryRules.ValidateChoices(new List<HistoryReorderChoice>
            { new() { OrderItemId = 1, Selected = true, Quantity = 0 } }));                        // quantity 0
        Assert.NotNull(CustomerOrderHistoryRules.ValidateChoices(new List<HistoryReorderChoice>
            { new() { OrderItemId = 1, Selected = true, Quantity = 1 },
              new() { OrderItemId = 1, Selected = true, Quantity = 2 } }));                       // same line twice
        Assert.Null(CustomerOrderHistoryRules.ValidateChoices(new List<HistoryReorderChoice>
            { new() { OrderItemId = 1, Selected = true, Quantity = 2 } }));                        // valid
    }
}

// ======================= INTEGRATION TESTS =======================

// Records every call to another member's service and returns a safe default,
// so only Member 3's code is under test. Handlers can override a method's result.
public class Stub : DispatchProxy
{
    public Dictionary<string, Func<object?[]?, object?>> On { get; } = new();
    public List<(string Name, object?[]? Args)> Calls { get; } = new();

    public static T Create<T>(out Stub stub) where T : class
    {
        T proxy = DispatchProxy.Create<T, Stub>();
        stub = (Stub)(object)proxy;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        Calls.Add((method!.Name, args));
        if (On.TryGetValue(method.Name, out var handler)) return handler(args);
        Type type = method.ReturnType;
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            Type inner = type.GetGenericArguments()[0];
            object? value = inner.IsValueType ? Activator.CreateInstance(inner) : null;
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, new[] { value });
        }
        return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }
}

public sealed class Member3Host : IDisposable
{
    public TestHost Host { get; } = new();
    public Stub Reservations { get; }
    public Stub Notifications { get; }
    public Stub StatusNotifier { get; }
    public CustomerCartService Carts { get; }
    public OrderService Orders { get; }
    public ApplicationUser Customer { get; private set; } = null!;
    public ApplicationUser OtherCustomer { get; private set; } = null!;
    public ApplicationUser StaffUser { get; private set; } = null!;
    public MenuItem Dish { get; private set; } = null!;
    public bool OrderingOpen { get; set; } = true;

    public Member3Host()
    {
        var reservations = Stub.Create<IReservationService>(out var r); Reservations = r;
        r.On[nameof(IReservationService.CanOrderAtAsync)] = _ => Task.FromResult(OrderingOpen);
        var notifications = Stub.Create<INotificationService>(out var n); Notifications = n;
        var notifier = Stub.Create<IOrderStatusNotifier>(out var s); StatusNotifier = s;
        var profiles = Stub.Create<IProfileService>(out _);
        var sessions = Stub.Create<IDiningSessionService>(out _);
        var environment = Stub.Create<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(out var env);
        env.On["get_ContentRootPath"] = _ => Path.GetTempPath();

        Carts = new CustomerCartService(Host.Db, Host.Get<IDataProtectionProvider>(),
            NullLogger<CustomerCartService>.Instance, profiles, new ReportService(null!),
            reservations, sessions, notifications);
        Orders = new OrderService(Host.Db, environment, NullLogger<OrderService>.Instance,
            new FakeBillingTransactionRepository(), notifier);
    }

    public async Task SeedAsync()
    {
        Customer = await Host.AddUserAsync("customer@test.com", Roles.Customer);
        OtherCustomer = await Host.AddUserAsync("other@test.com", Roles.Customer);
        StaffUser = await Host.AddUserAsync("staff@test.com", Roles.Staff);
        var category = new MenuCategory { Name = "Sashimi", Code = "SS", DisplayOrder = 1 };
        Dish = new MenuItem { Category = category, Name = "Salmon Sashimi", Code = "SS01", Price = 25.00m, IsAvailable = true };
        Host.Db.MenuItems.Add(Dish);
        Host.Db.RestaurantSettings.Add(new RestaurantSetting
            { TaxRatePercent = 6m, ServiceChargePercent = 10m, ServiceChargeOnTakeaway = false });
        await Host.Db.SaveChangesAsync();
    }

    // Adds the dish to the cart, switches the cart to takeaway and returns the review (quote).
    public async Task<CheckoutQuote> ReviewTakeawayAsync(int quantity)
    {
        Assert.True((await Carts.AddAsync(Customer.Id, Dish.Id, quantity, null)).Succeeded);
        var cart = await Carts.GetAsync(Customer.Id);
        var update = new CartUpdate
        {
            Revision = cart.Revision,
            OrderType = OrderType.Takeaway,
            Lines = cart.Lines.Select(l => new CartLineInput { Id = l.Id, Quantity = l.Quantity, Notes = l.Notes }).ToList()
        };
        Assert.True((await Carts.UpdateAsync(Customer.Id, update)).Succeeded);
        cart = await Carts.GetAsync(Customer.Id);
        return await Carts.PrepareAsync(Customer.Id, cart.Revision);
    }

    public async Task<int> PlaceTakeawayAsync(int quantity)
    {
        var quote = await ReviewTakeawayAsync(quantity);
        Assert.True(quote.Succeeded, quote.Message);
        var placed = await Carts.PlaceAsync(Customer.Id, quote.Token);
        Assert.True(placed.Succeeded, placed.Message);
        return placed.OrderId!.Value;
    }

    public void Dispose() => Host.Dispose();
}

// ---------- US24: cart to order (CustomerCartService + database + Member 2/4/5 contracts) ----------
public class CheckoutIntegrationTests : IAsyncLifetime
{
    private readonly Member3Host _m = new();
    public Task InitializeAsync() => _m.SeedAsync();
    public Task DisposeAsync() { _m.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task IT_M3_01_PlacingATakeawayOrder_SavesOrderItemsAndChargesAndEmptiesTheCart()
    {
        int orderId = await _m.PlaceTakeawayAsync(2);

        var order = await _m.Host.Db.Orders.Include(o => o.Items).AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(50.00m, order.Subtotal);
        Assert.Equal(0.00m, order.ServiceCharge);          // no service charge on takeaway (Member 5 setting)
        Assert.Equal(3.00m, order.TaxAmount);              // 6% tax
        Assert.Equal(53.00m, order.TotalAmount);
        Assert.Equal(6m, order.TaxRatePercent);            // the rate is saved with the order
        var item = Assert.Single(order.Items);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(25.00m, item.UnitPrice);
        Assert.Equal(0, await _m.Carts.CountAsync(_m.Customer.Id));
    }

    [Fact]
    public async Task IT_M3_02_NewOrder_IsSentToMember4NotificationsAsPending()
    {
        int orderId = await _m.PlaceTakeawayAsync(1);

        var call = Assert.Single(_m.Notifications.Calls, c => c.Name == nameof(INotificationService.NotifyOrderStatusAsync));
        Assert.Equal((object)orderId, call.Args![0]);
        Assert.Equal((object)OrderStatus.Pending, call.Args[1]);
    }

    [Fact]
    public async Task IT_M3_03_RunningPromotion_IsChargedAndTheNormalPriceIsKept()
    {
        _m.Host.Db.Promotions.Add(new Promotion { MenuItemId = _m.Dish.Id, Title = "Salmon week", DiscountPercent = 20m,
            StartDate = DateTime.Now.AddDays(-1), EndDate = DateTime.Now.AddDays(3), IsActive = true });
        await _m.Host.Db.SaveChangesAsync();

        int orderId = await _m.PlaceTakeawayAsync(1);

        var item = await _m.Host.Db.OrderItems.AsNoTracking().SingleAsync(i => i.OrderId == orderId);
        Assert.Equal(20.00m, item.UnitPrice);
        Assert.Equal(25.00m, item.NormalUnitPrice);
    }

    [Fact]
    public async Task IT_M3_04_ConfirmingTheSameReviewTwice_CreatesOnlyOneOrder()
    {
        var quote = await _m.ReviewTakeawayAsync(1);

        var first = await _m.Carts.PlaceAsync(_m.Customer.Id, quote.Token);
        var second = await _m.Carts.PlaceAsync(_m.Customer.Id, quote.Token);   // double click / refresh

        Assert.True(first.Succeeded && second.Succeeded);
        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(1, await _m.Host.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task IT_M3_05_OrderingClosed_BlocksCheckout()
    {
        _m.OrderingOpen = false;   // Member 2's CanOrderAtAsync says the kitchen is closed

        var quote = await _m.ReviewTakeawayAsync(1);

        Assert.False(quote.Succeeded);
        Assert.Contains("closed", quote.Message);
        Assert.Equal(0, await _m.Host.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task IT_M3_06_PriceChangedAfterReview_IsNotChargedSilently()
    {
        var quote = await _m.ReviewTakeawayAsync(1);
        var dish = await _m.Host.Db.MenuItems.SingleAsync(d => d.Id == _m.Dish.Id);
        dish.Price = 30.00m;                         // admin changes the price before the customer confirms
        await _m.Host.Db.SaveChangesAsync();
        _m.Host.Db.ChangeTracker.Clear();

        var placed = await _m.Carts.PlaceAsync(_m.Customer.Id, quote.Token);

        Assert.Equal(CartResult.Changed, placed.Status);
        Assert.Equal(0, await _m.Host.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task IT_M3_07_ACustomerCannotReadAnotherCustomersOrderStatus()
    {
        int orderId = await _m.PlaceTakeawayAsync(1);

        Assert.Equal(OrderStatus.Pending, await _m.Carts.GetOrderStatusAsync(_m.Customer.Id, orderId));
        Assert.Null(await _m.Carts.GetOrderStatusAsync(_m.OtherCustomer.Id, orderId));
    }
}

// ---------- US25 / US26 / US27: staff status changes (OrderService + database + notifier) ----------
public class StaffStatusIntegrationTests : IAsyncLifetime
{
    private readonly Member3Host _m = new();
    private int _orderId;

    public async Task InitializeAsync()
    {
        await _m.SeedAsync();
        _orderId = await _m.PlaceTakeawayAsync(1);
        _m.Host.Db.ChangeTracker.Clear();
    }
    public Task DisposeAsync() { _m.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task IT_M3_08_StaffAcceptsOrder_StatusAndHistoryAreSaved_ThenCustomerIsNotified()
    {
        var result = await _m.Orders.ChangeStaffOrderStatusAsync(_orderId, OrderStatus.Pending, OrderStatus.Preparing, null, _m.StaffUser.Id);

        Assert.Equal(StaffStatusOutcome.Success, result.Outcome);
        Assert.Equal(OrderStatus.Preparing, await _m.Host.Db.Orders.Where(o => o.Id == _orderId).Select(o => o.Status).SingleAsync());
        var log = await _m.Host.Db.OrderStatusLogs.SingleAsync(l => l.OrderId == _orderId);
        Assert.Equal(OrderStatus.Pending, log.FromStatus);
        Assert.Equal(OrderStatus.Preparing, log.ToStatus);
        Assert.Contains(_m.StatusNotifier.Calls, c => c.Name == nameof(IOrderStatusNotifier.OrderStatusChangedAsync));
    }

    [Fact]
    public async Task IT_M3_09_StaleScreen_GetsAConflictAndNothingChanges()
    {
        await _m.Orders.ChangeStaffOrderStatusAsync(_orderId, OrderStatus.Pending, OrderStatus.Preparing, null, _m.StaffUser.Id);
        _m.StatusNotifier.Calls.Clear();

        // A second staff member still sees "Pending" and presses Accept again.
        var result = await _m.Orders.ChangeStaffOrderStatusAsync(_orderId, OrderStatus.Pending, OrderStatus.Preparing, null, _m.StaffUser.Id);

        Assert.Equal(StaffStatusOutcome.Conflict, result.Outcome);
        Assert.Equal(1, await _m.Host.Db.OrderStatusLogs.CountAsync(l => l.OrderId == _orderId));
        Assert.Empty(_m.StatusNotifier.Calls);
    }

    [Fact]
    public async Task IT_M3_10_CustomerAccount_CannotChangeOrderStatus()
    {
        var result = await _m.Orders.ChangeStaffOrderStatusAsync(_orderId, OrderStatus.Pending, OrderStatus.Preparing, null, _m.Customer.Id);

        Assert.Equal(StaffStatusOutcome.Forbidden, result.Outcome);
        Assert.Equal(OrderStatus.Pending, await _m.Host.Db.Orders.Where(o => o.Id == _orderId).Select(o => o.Status).SingleAsync());
    }

    [Fact]
    public async Task IT_M3_11_OrderWithPendingPayment_CannotBeCancelled()
    {
        _m.Host.Db.Payments.Add(new Payment { OrderId = _orderId, Amount = 26.50m, Method = PaymentMethod.Online, Status = PaymentStatus.Pending });
        await _m.Host.Db.SaveChangesAsync();

        var result = await _m.Orders.ChangeStaffOrderStatusAsync(_orderId, OrderStatus.Pending, OrderStatus.Cancelled, null, _m.StaffUser.Id);

        Assert.Equal(StaffStatusOutcome.Locked, result.Outcome);
        Assert.Equal(OrderStatus.Pending, await _m.Host.Db.Orders.Where(o => o.Id == _orderId).Select(o => o.Status).SingleAsync());
    }
}
