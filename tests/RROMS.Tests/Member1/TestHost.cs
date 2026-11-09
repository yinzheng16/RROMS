// Owner: Member 1 (Chan Yin Zheng). Shared test set-up for the Identity, Access & AI Chatbot tests.
// Real ASP.NET Core Identity + the real ApplicationDbContext on a temporary in-memory SQLite database.
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RROMS.Data;
using RROMS.Models;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Tests.Member1;

// Records every email instead of sending it through Gmail.
public sealed class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Html)> Sent { get; } = new();
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        Sent.Add((email, subject, htmlMessage));
        return Task.CompletedTask;
    }
}

// Only GetInfoAsync is needed (phone number and email footer); the rest are not used by these tests.
public sealed class FakeHomeService : IHomeService
{
    public const string Phone = "+60 3-0000 0000";
    public Task<HomeInfo> GetInfoAsync() =>
        Task.FromResult(new HomeInfo("Test address", Phone, "open", "Open now", 80, 16, 7, "9:30 PM"));
    public Task<List<HomeDish>> GetSignatureDishesAsync(int count = 3) => Task.FromResult(new List<HomeDish>());
    public Task<List<HomeOffer>> GetActiveOffersAsync(int count = 3) => Task.FromResult(new List<HomeOffer>());
    public Task<int> CountOfferDishesAsync() => Task.FromResult(0);
    public Task<List<HomeHoursRow>> GetOpeningHoursAsync() => Task.FromResult(new List<HomeHoursRow>());
    public Task<List<OmikujiDish>> GetOmikujiDishesAsync() => Task.FromResult(new List<OmikujiDish>());
    public Task<BookingSetup> GetBookingSetupAsync() => throw new NotSupportedException();
}

// The real SignInManager, except that it records the login cookie settings instead of writing a cookie
// (there is no browser in a unit test).
public sealed class RecordingSignInManager : SignInManager<ApplicationUser>
{
    public RecordingSignInManager(UserManager<ApplicationUser> userManager, IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory, IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger, IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation) { }

    public ApplicationUser? SignedInUser { get; private set; }
    public AuthenticationProperties? LastProperties { get; private set; }

    public override Task SignInWithClaimsAsync(ApplicationUser user, AuthenticationProperties? authenticationProperties,
        IEnumerable<Claim> additionalClaims)
    {
        SignedInUser = user;
        LastProperties = authenticationProperties;
        return Task.CompletedTask;
    }

    public override Task RefreshSignInAsync(ApplicationUser user) => Task.CompletedTask;
    public override Task SignOutAsync() { SignedInUser = null; return Task.CompletedTask; }
}

public sealed class TestHost : IDisposable
{
    public const string Password = "Test@1234";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _root;
    private readonly IServiceScope _scope;

    public FakeEmailSender Emails { get; } = new();

    public TestHost()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddMemoryCache();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));

        // Same Identity rules as Program.cs (US02, US52).
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
                options.Password.RequiredLength = 8;
                options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderOptions.ProviderName;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasswordResetTokenProvider<ApplicationUser>>(PasswordResetTokenProviderOptions.ProviderName)
            .AddSignInManager<RecordingSignInManager>();

        services.AddSingleton<IEmailSender>(Emails);
        services.AddSingleton<IHomeService, FakeHomeService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<AccountSecurityService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<ChatFaqService>();

        _root = services.BuildServiceProvider();
        _scope = _root.CreateScope();

        Db.Database.EnsureCreated();
        var roles = Get<RoleManager<IdentityRole>>();
        foreach (var role in new[] { Roles.Admin, Roles.Staff, Roles.Customer })
            roles.CreateAsync(new IdentityRole(role)).GetAwaiter().GetResult();
    }

    public T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();
    public ApplicationDbContext Db => Get<ApplicationDbContext>();
    public UserManager<ApplicationUser> Users => Get<UserManager<ApplicationUser>>();
    public RecordingSignInManager SignIn => (RecordingSignInManager)Get<SignInManager<ApplicationUser>>();
    public IAccountService Accounts => Get<IAccountService>();

    public async Task<ApplicationUser> AddUserAsync(string email, string role, bool active = true)
    {
        var user = new ApplicationUser { UserName = email, Email = email, FullName = role + " Tester", IsActive = active };
        var created = await Users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Select(e => e.Description)));
        await Users.AddToRoleAsync(user, role);
        return user;
    }

    public static ClaimsPrincipal PrincipalFor(ApplicationUser user) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) }, "Test"));

    public void Dispose()
    {
        _scope.Dispose();
        _root.Dispose();
        _connection.Dispose();
    }
}
