using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RROMS.Data;
using RROMS.Hubs;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Tests;

// QA01 (Member 4): US37/US38 notification pipeline.
// A notification is extra work for the caller (a booking, an order, a
// payment): a failed live push or a failed email must never make the
// caller fail, and the in-app notification must still be saved.
public class NotificationServiceTests : IDisposable
{
    private readonly SqliteTestDb _test = new();

    private NotificationService CreateService(
        ApplicationDbContext db,
        IHubContext<NotificationHub> hub,
        IEmailSender email,
        IServiceScopeFactory scopes)
    {
        var users = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(db),
            null!, null!, null!, null!, null!, null!, null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        return new NotificationService(
            new Repository<Notification>(db),
            new Repository<Reservation>(db),
            new Repository<Order>(db),
            users,
            email,
            new BrokenHomeService(),
            new HttpContextAccessor(),
            NullLogger<NotificationService>.Instance,
            hub,
            scopes);
    }

    [Fact]
    public async Task LivePushFailure_StillSavesTheInAppNotification()
    {
        var customer = await TestData.CreateUserAsync(_test.Db);
        var hub = new FakeHubContext { Throw = true };

        var service = CreateService(_test.Db, hub, new FailingEmailSender(),
            new BrokenScopeFactory());

        // Must not throw, even though SignalR and the email worker both fail.
        await service.SendAsync(customer.Id, "Payment received",
            "We received RM 25.00 for bill YB-000001.", NotificationType.Payment);

        var saved = await _test.Db.Notifications.AsNoTracking()
            .SingleAsync(n => n.UserId == customer.Id);

        Assert.Equal("Payment received", saved.Title);
        Assert.False(saved.IsRead);
        Assert.Equal(1, hub.Attempts);
    }

    [Fact]
    public async Task EmailFailure_DoesNotStopTheCaller()
    {
        var customer = await TestData.CreateUserAsync(_test.Db);
        var email = new FailingEmailSender();

        // The background email worker gets its own scope and database
        // context, as in the real application.
        var services = new ServiceCollection();
        services.AddScoped<INotificationService>(_ =>
            CreateService(_test.NewContext(), new FakeHubContext(), email, new BrokenScopeFactory()));
        using var provider = services.BuildServiceProvider();

        var service = CreateService(_test.Db, new FakeHubContext(), email,
            provider.GetRequiredService<IServiceScopeFactory>());

        await service.SendAsync(customer.Id, "Payment received",
            "We received RM 25.00 for bill YB-000001.", NotificationType.Payment);

        // The caller has already returned; wait for the background email attempt.
        var attempted = await Task.WhenAny(email.Attempted.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(email.Attempted.Task, attempted);
        Assert.Equal(customer.Email, await email.Attempted.Task);
        Assert.True(await _test.Db.Notifications.AnyAsync(n => n.UserId == customer.Id));
    }

    public void Dispose() => _test.Dispose();

    // ---------------- test doubles ----------------

    // Gmail is unreachable: every send fails after recording the address.
    private sealed class FailingEmailSender : IEmailSender
    {
        public TaskCompletionSource<string> Attempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Attempted.TrySetResult(email);
            throw new InvalidOperationException("SMTP authentication failed (test).");
        }
    }

    // Restaurant details are not needed; NotificationService falls back to the brand name.
    private sealed class BrokenHomeService : IHomeService
    {
        public Task<List<HomeDish>> GetSignatureDishesAsync(int count = 3) => throw new NotImplementedException();
        public Task<List<HomeOffer>> GetActiveOffersAsync(int count = 3) => throw new NotImplementedException();
        public Task<int> CountOfferDishesAsync() => throw new NotImplementedException();
        public Task<List<HomeHoursRow>> GetOpeningHoursAsync() => throw new NotImplementedException();
        public Task<HomeInfo> GetInfoAsync() => throw new NotImplementedException();
        public Task<List<OmikujiDish>> GetOmikujiDishesAsync() => throw new NotImplementedException();
        public Task<BookingSetup> GetBookingSetupAsync() => throw new NotImplementedException();
    }

    private sealed class BrokenScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new InvalidOperationException("No background scope (test).");
    }

    // SignalR hub that counts pushes and can simulate a dropped connection.
    private sealed class FakeHubContext : IHubContext<NotificationHub>
    {
        private readonly FakeClients _clients;

        public FakeHubContext() => _clients = new FakeClients(this);

        public bool Throw { get; set; }
        public int Attempts { get; set; }

        public IHubClients Clients => _clients;
        public IGroupManager Groups => throw new NotImplementedException();
    }

    private sealed class FakeClients : IHubClients, IClientProxy
    {
        private readonly FakeHubContext _hub;

        public FakeClients(FakeHubContext hub) => _hub = hub;

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            _hub.Attempts++;

            if (_hub.Throw)
                throw new InvalidOperationException("SignalR connection lost (test).");

            return Task.CompletedTask;
        }

        public IClientProxy All => this;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => this;
        public IClientProxy Client(string connectionId) => this;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => this;
        public IClientProxy Group(string groupName) => this;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => this;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => this;
        public IClientProxy User(string userId) => this;
        public IClientProxy Users(IReadOnlyList<string> userIds) => this;
    }
}
