// Owner: Member 1 (Chan Yin Zheng). INTEGRATION TESTS: my services working together with the real
// ASP.NET Core Identity (password hashing, lockout, roles, tokens) and the real database schema.
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Services;
using RROMS.Services.Interfaces;

namespace RROMS.Tests.Member1;

// ---------- US01 registration, US02 login, US51 starting page ----------
public class AccountIntegrationTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task IT01_Register_CreatesCustomerWithHashedPassword()
    {
        var result = await _host.Accounts.RegisterCustomerAsync("New Guest", "guest@test.com", "+60123456789", TestHost.Password);

        Assert.True(result.Succeeded);
        var user = await _host.Users.FindByEmailAsync("guest@test.com");
        Assert.NotNull(user);
        Assert.True(await _host.Users.IsInRoleAsync(user!, Roles.Customer));
        Assert.NotEqual(TestHost.Password, user!.PasswordHash);   // only the hash is stored
    }

    [Fact]
    public async Task IT02_Register_DuplicateEmail_IsRejected()
    {
        await _host.Accounts.RegisterCustomerAsync("First", "same@test.com", "+60123456789", TestHost.Password);
        var second = await _host.Accounts.RegisterCustomerAsync("Second", "same@test.com", "+60123456780", TestHost.Password);

        Assert.False(second.Succeeded);
        Assert.Contains(second.Errors, e => e.Code is "DuplicateEmail" or "DuplicateUserName");
    }

    [Fact]
    public async Task IT03_Register_WeakPassword_IsRejected()
    {
        var result = await _host.Accounts.RegisterCustomerAsync("Weak", "weak@test.com", "+60123456789", "abc123");

        Assert.False(result.Succeeded);
        Assert.Null(await _host.Users.FindByEmailAsync("weak@test.com"));
    }

    [Fact]
    public async Task IT04_Login_CorrectPassword_SignsInForThisSessionOnly()
    {
        await _host.AddUserAsync("cust@test.com", Roles.Customer);

        var result = await _host.Accounts.LoginAsync("cust@test.com", TestHost.Password, rememberMe: false);

        Assert.Equal(LoginResult.Success, result);
        Assert.False(_host.SignIn.LastProperties!.IsPersistent);
    }

    [Fact]
    public async Task IT05_Login_CustomerKeepMeLoggedIn_LastsSevenDays()
    {
        await _host.AddUserAsync("cust@test.com", Roles.Customer);

        await _host.Accounts.LoginAsync("cust@test.com", TestHost.Password, rememberMe: true);

        var props = _host.SignIn.LastProperties!;
        Assert.True(props.IsPersistent);
        Assert.True(props.ExpiresUtc > DateTimeOffset.UtcNow.AddDays(6.9));
    }

    [Fact]
    public async Task IT06_Login_StaffKeepMeLoggedIn_IsIgnored()
    {
        await _host.AddUserAsync("staff@test.com", Roles.Staff);

        await _host.Accounts.LoginAsync("staff@test.com", TestHost.Password, rememberMe: true);

        Assert.False(_host.SignIn.LastProperties!.IsPersistent);   // shared counter computer
    }

    [Fact]
    public async Task IT07_Login_UnknownEmail_GivesSameMessageAsWrongPassword()
    {
        var result = await _host.Accounts.LoginAsync("nobody@test.com", TestHost.Password, false);

        Assert.Equal(LoginResult.InvalidCredentials, result);
        Assert.Null(_host.SignIn.SignedInUser);
    }

    [Fact]
    public async Task IT08_Login_WrongPassword_IsCounted()
    {
        var user = await _host.AddUserAsync("cust@test.com", Roles.Customer);

        var result = await _host.Accounts.LoginAsync("cust@test.com", "Wrong@1234", false);

        Assert.Equal(LoginResult.InvalidCredentials, result);
        Assert.Equal(1, await _host.Users.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task IT09_Login_FiveWrongPasswords_LocksAccount()
    {
        await _host.AddUserAsync("cust@test.com", Roles.Customer);

        for (var i = 1; i <= 4; i++)
            Assert.Equal(LoginResult.InvalidCredentials, await _host.Accounts.LoginAsync("cust@test.com", "Wrong@1234", false));
        Assert.Equal(LoginResult.LockedOut, await _host.Accounts.LoginAsync("cust@test.com", "Wrong@1234", false));

        // Even the right password is refused while the account is locked.
        Assert.Equal(LoginResult.LockedOut, await _host.Accounts.LoginAsync("cust@test.com", TestHost.Password, false));
    }

    [Fact]
    public async Task IT10_Login_DeactivatedAccount_IsRefused()
    {
        await _host.AddUserAsync("old@test.com", Roles.Staff, active: false);

        var result = await _host.Accounts.LoginAsync("old@test.com", TestHost.Password, false);

        Assert.Equal(LoginResult.Inactive, result);
        Assert.Null(_host.SignIn.SignedInUser);
    }

    [Theory]
    [InlineData(Roles.Admin, "/Admin")]
    [InlineData(Roles.Staff, "/Staff/Reservations")]
    [InlineData(Roles.Customer, "/")]
    public async Task IT11_StartingPage_FollowsRole(string role, string expected)
    {
        await _host.AddUserAsync("user@test.com", role);

        Assert.Equal(expected, await _host.Accounts.GetStartPageAsync("user@test.com"));
    }

    public void Dispose() => _host.Dispose();
}

// ---------- US05, US07: user and role management ----------
public class UserAdminIntegrationTests : IDisposable
{
    private readonly TestHost _host = new();
    private static string Link(string id, string code, bool welcome) => $"https://test/set?u={id}";

    [Fact]
    public async Task IT12_DeactivateStaff_BlocksLoginAndIsLogged()
    {
        var admin = await _host.AddUserAsync("admin@test.com", Roles.Admin);
        var staff = await _host.AddUserAsync("staff@test.com", Roles.Staff);
        var stampBefore = staff.SecurityStamp;

        var result = await _host.Get<UserAdminService>().SetActiveAsync(TestHost.PrincipalFor(admin), staff.Id, false);

        Assert.True(result.Ok);
        var saved = await _host.Db.Users.AsNoTracking().SingleAsync(u => u.Id == staff.Id);
        Assert.False(saved.IsActive);
        Assert.NotEqual(stampBefore, saved.SecurityStamp);   // old logins are signed out within a minute
        Assert.True(await _host.Db.UserAdminLogs.AnyAsync(l => l.TargetUserId == staff.Id && l.Action == UserAdminAction.Deactivated));
        Assert.Equal(LoginResult.Inactive, await _host.Accounts.LoginAsync("staff@test.com", TestHost.Password, false));
    }

    [Fact]
    public async Task IT13_AdminCannotDeactivateOwnAccount()
    {
        var admin = await _host.AddUserAsync("admin@test.com", Roles.Admin);

        var result = await _host.Get<UserAdminService>().SetActiveAsync(TestHost.PrincipalFor(admin), admin.Id, false);

        Assert.False(result.Ok);
        Assert.True((await _host.Users.FindByIdAsync(admin.Id))!.IsActive);
    }

    [Fact]
    public async Task IT14_CreateStaff_HasNoPasswordAndGetsSetPasswordEmail()
    {
        var admin = await _host.AddUserAsync("admin@test.com", Roles.Admin);

        var result = await _host.Get<UserAdminService>().CreateUserAsync(TestHost.PrincipalFor(admin),
            new NewUserInput("Lim Mei Ling", "meiling@test.com", null, null, "Staff", null), Link);

        Assert.True(result.Ok, result.Message);
        var user = await _host.Users.FindByEmailAsync("meiling@test.com");
        Assert.NotNull(user);
        Assert.True(await _host.Users.IsInRoleAsync(user!, Roles.Staff));
        Assert.False(await _host.Users.HasPasswordAsync(user!));          // the Admin never knows a password
        Assert.Contains(_host.Emails.Sent, e => e.To == "meiling@test.com");
        Assert.True(await _host.Db.UserAdminLogs.AnyAsync(l => l.TargetUserId == user!.Id && l.Action == UserAdminAction.Created));
    }

    [Fact]
    public async Task IT15_FourthActiveAdmin_IsRefused()
    {
        var admin = await _host.AddUserAsync("admin1@test.com", Roles.Admin);
        await _host.AddUserAsync("admin2@test.com", Roles.Admin);
        await _host.AddUserAsync("admin3@test.com", Roles.Admin);

        var result = await _host.Get<UserAdminService>().CreateUserAsync(TestHost.PrincipalFor(admin),
            new NewUserInput("Fourth Admin", "admin4@test.com", null, null, "Admin", TestHost.Password), Link);

        Assert.False(result.Ok);
        Assert.Contains("at most 3", result.Message);
        Assert.Null(await _host.Users.FindByEmailAsync("admin4@test.com"));
    }

    [Fact]
    public async Task IT16_NewAdmin_NeedsTheAdminsOwnPassword()
    {
        var admin = await _host.AddUserAsync("admin1@test.com", Roles.Admin);

        var result = await _host.Get<UserAdminService>().CreateUserAsync(TestHost.PrincipalFor(admin),
            new NewUserInput("Second Admin", "admin2@test.com", null, null, "Admin", "Wrong@1234"), Link);

        Assert.False(result.Ok);
        Assert.Equal("MyPassword", result.Field);
        Assert.Null(await _host.Users.FindByEmailAsync("admin2@test.com"));
    }

    public void Dispose() => _host.Dispose();
}

// ---------- US52 forgot password, US03/US04 change password ----------
public class AccountSecurityIntegrationTests : IDisposable
{
    private readonly TestHost _host = new();
    private AccountSecurityService Security => _host.Get<AccountSecurityService>();

    [Fact]
    public async Task IT17_ResetRequest_UnknownEmail_SendsNothingButLooksTheSame()
    {
        var result = await Security.RequestPasswordResetAsync("nobody@test.com", (id, code) => "https://test/reset");

        Assert.True(result.Ok);
        Assert.Empty(_host.Emails.Sent);
    }

    [Fact]
    public async Task IT18_ResetPassword_FullFlow_LinkWorksOnlyOnce()
    {
        await _host.AddUserAsync("cust@test.com", Roles.Customer);
        string userId = "", code = "";

        var request = await Security.RequestPasswordResetAsync("cust@test.com", (id, c) => { userId = id; code = c; return "https://test/reset"; });
        Assert.True(request.Ok);
        Assert.Single(_host.Emails.Sent);

        var reset = await Security.ResetPasswordAsync(userId, code, "NewPass@123", "NewPass@123", "https://test/forgot");
        Assert.True(reset.Ok, reset.Message);
        Assert.Equal(LoginResult.Success, await _host.Accounts.LoginAsync("cust@test.com", "NewPass@123", false));
        Assert.Contains(_host.Emails.Sent, e => e.Subject.Contains("changed", StringComparison.OrdinalIgnoreCase));

        var again = await Security.ResetPasswordAsync(userId, code, "Other@1234", "Other@1234", "https://test/forgot");
        Assert.False(again.Ok);   // single-use: the security stamp changed
    }

    [Fact]
    public async Task IT19_ResetPassword_SameAsOldPassword_IsRejected()
    {
        await _host.AddUserAsync("cust@test.com", Roles.Customer);
        string userId = "", code = "";
        await Security.RequestPasswordResetAsync("cust@test.com", (id, c) => { userId = id; code = c; return "x"; });

        var result = await Security.ResetPasswordAsync(userId, code, TestHost.Password, TestHost.Password, "x");

        Assert.False(result.Ok);
        Assert.Equal("NewPassword", result.Field);
    }

    [Fact]
    public async Task IT20_ChangePassword_WrongCurrentPassword_IsCounted()
    {
        var user = await _host.AddUserAsync("cust@test.com", Roles.Customer);

        var result = await Security.ChangePasswordAsync(TestHost.PrincipalFor(user), "Wrong@1234", "NewPass@123", "NewPass@123", "x");

        Assert.False(result.Ok);
        Assert.Equal("CurrentPassword", result.Field);
        Assert.Equal(1, await _host.Users.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task IT21_ChangePassword_Valid_ChangesItAndSendsAlert()
    {
        var user = await _host.AddUserAsync("cust@test.com", Roles.Customer);

        var result = await Security.ChangePasswordAsync(TestHost.PrincipalFor(user), TestHost.Password, "NewPass@123", "NewPass@123", "x");

        Assert.True(result.Ok, result.Message);
        Assert.True(await _host.Users.CheckPasswordAsync(user, "NewPass@123"));
        Assert.Single(_host.Emails.Sent);
    }

    public void Dispose() => _host.Dispose();
}
