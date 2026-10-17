using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RROMS.Data;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services;
using RROMS.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// ---------- Data access tier ----------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

// ---------- Identity: login, roles, lockout (Member 1) ----------
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;   // email confirmation added later by Member 4
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

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
builder.Services.AddScoped<INotificationService, NotificationService>();   // EN06 / Member 4
// Member 1: builder.Services.AddScoped<IAccountService, AccountService>();
// Member 2: builder.Services.AddScoped<IReservationService, ReservationService>();
// Member 3: builder.Services.AddScoped<IOrderService, OrderService>();
// Member 4: builder.Services.AddScoped<IPaymentService, PaymentService>();
// Member 5: builder.Services.AddScoped<IReportService, ReportService>();

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

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
