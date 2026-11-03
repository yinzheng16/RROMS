using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services;
using RROMS.Services.Interfaces;
using RROMS.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ---------- Data access tier ----------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

// US32: Database transaction and locking for Bill creation.
builder.Services.AddScoped<
    IBillingTransactionRepository,
    BillingTransactionRepository>();

// ---------- Identity: login, roles, lockout (Member 1) ----------
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;   // email confirmation added later by Member 4
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
        options.Password.RequiredLength = 8;
        // US52: reset links use their own 1-hour token provider (change-email links keep 24 hours)
        options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderOptions.ProviderName;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddTokenProvider<PasswordResetTokenProvider<ApplicationUser>>(PasswordResetTokenProviderOptions.ProviderName);

// ---------- Login cookie: auto logout (Member 1, US02) ----------
builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);   // not ticked: logged out after 30 minutes of doing nothing
    options.SlidingExpiration = true;                     // every visit/click restarts the 30 minutes
    // "Keep me logged in" ticked: 7 days instead (set in AccountService.LoginAsync)
});

// ---------- US05 / US07 (Member 1): role changes and deactivation reach logged-in users quickly ----------
// Every minute each login is re-checked against the database: a new role shows on the next page,
// and a deactivated account (new security stamp) is logged out.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(1);
});


// ---------- Role-based access control (Member 1) ----------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole(Roles.Admin));
    options.AddPolicy("StaffOrAdmin", p => p.RequireRole(Roles.Staff, Roles.Admin));
    options.AddPolicy("CustomerOnly", p => p.RequireRole(Roles.Customer));
});

// ---------- Presentation tier ----------
builder.Services.AddRazorPages(options =>
{
    // Every page inside these folders is protected automatically.
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
    options.Conventions.AuthorizeFolder("/Staff", "StaffOrAdmin");
    options.Conventions.AuthorizeFolder("/Customer", "CustomerOnly");
});

// ---------- Business logic tier (register each module's services here) ----------
// EN07 (Member 1): shared email sender. Settings in appsettings.json ("Email"),
// the Gmail App password ONLY in User Secrets ("Email:Password"). No password = emails go to the Output window.
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection(EmailSettings.Section));
builder.Services.AddSingleton<EmailOutbox>();
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();   // EN06 / Member 4
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IProfileService, ProfileService>();   // Member 1: US03 / US04 My profile
builder.Services.AddScoped<IAccountSecurityService, AccountSecurityService>();   // Member 1: US03 / US04 change password / email
builder.Services.AddScoped<IUserAdminService, UserAdminService>();   // Member 1: US05 / US07 Admin → Users

// US08 / US10 (Member 1): AI chatbot. Settings in appsettings.json ("Ai"),
// the API key ONLY in User Secrets ("Ai:ApiKey"). No key = the chatbot answers from FAQ keywords only.
builder.Services.Configure<AiSettings>(builder.Configuration.GetSection(AiSettings.Section));
builder.Services.AddHttpClient(ChatbotService.HttpClientName);
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IChatbotService, ChatbotService>();
builder.Services.AddScoped<IChatFaqService, ChatFaqService>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IMyReservationsService, ReservationService>();
builder.Services.AddScoped<IHomeService, HomeService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICustomerCartService, CustomerCartService>(); // Member 3 / US24
builder.Services.AddScoped<IPromotionService, PromotionService>(); // Member 3 / US29
// SignalR (shared, set up by Member 3 for US27; Member 4 adds NotificationHub for US37)
builder.Services.AddSignalR();
builder.Services.AddScoped<IOrderStatusNotifier, OrderStatusNotifier>(); // Member 3 / US27
builder.Services.AddScoped<IBillingService, BillingService>(); // Member 4 / US32
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IDiningSessionService, DiningSessionService>();   // Member 2: dining sessions
builder.Services.AddScoped<IStaffReservationService, StaffReservationService>();   // Member 2: US16 staff reservations
builder.Services.AddScoped<IOccupancyAnalyticsService, OccupancyAnalyticsService>();   // Member 2: US20 occupancy analytics

//Member 5
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ISalesReportService, SalesReportService>();   // Member 5: US44 Filtered reports
var app = builder.Build();

// Apply migrations and create the roles and test accounts.
using (var scope = app.Services.CreateScope())
{
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// US03 / US04 (Member 1): built-in Identity pages we don't use go back to My profile
// (two-factor authentication, download/delete personal data, external logins).
var hiddenManagePages = new[]
{
    "TwoFactorAuthentication", "EnableAuthenticator", "Disable2fa", "GenerateRecoveryCodes",
    "ResetAuthenticator", "ShowRecoveryCodes", "PersonalData", "DownloadPersonalData",
    "DeletePersonalData", "ExternalLogins",
    // US03 / US04: replaced by the Security pop-ups on My profile
    "ChangePassword", "SetPassword", "Email"
};
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (hiddenManagePages.Any(p => path.StartsWithSegments("/Identity/Account/Manage/" + p, StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.Redirect("/Identity/Account/Manage");
        return;
    }
    await next();
});

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapHub<OrderStatusHub>(OrderStatusHub.Path); // Member 3 / US27 live order status

app.Run();
