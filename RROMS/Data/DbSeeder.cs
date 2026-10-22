using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;

namespace RROMS.Data
{
    // Runs once at start-up. Each part is skipped if the data already exists,
    // so nothing is duplicated and data added through the system is kept.
    //
    // Sample data for each module is added here by its owner, for example:
    //   Member 2: dining areas, tables, opening hours
    //   Member 3: menu categories and items
    //   Member 5: restaurant settings
    //   Member 1: chatbot FAQs
    // Data written here reaches every teammate; data typed into the website stays on one laptop.
    public static class DbSeeder
    {
        // Test password for the seeded accounts (development only).
        public const string DefaultPassword = "Test@123";

        public static async Task SeedAsync(IServiceProvider services)
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

            // Creates or updates the database to the latest migration.
            await db.Database.MigrateAsync();

            // ---------- Roles (required: role-based access depends on them) ----------
            foreach (var role in new[] { Roles.Admin, Roles.Staff, Roles.Customer })
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            // ---------- First admin (required: nobody can create the first admin from the website) ----------
            await CreateUserAsync(userManager, "admin@rroms.com", "System Admin", Roles.Admin);

            // ---------- Test accounts (for development and the demo; can be removed later) ----------
            await CreateUserAsync(userManager, "staff@rroms.com", "Restaurant Staff", Roles.Staff);
            await CreateUserAsync(userManager, "customer@rroms.com", "Test Customer", Roles.Customer);

            // ---------- Module sample data goes below this line ----------
            ReservationSeed.Seed(db);

            // ---------- Member 3: sample menu item for US21 deletion testing ----------
            var sampleCategory = await db.MenuCategories
                .FirstOrDefaultAsync(c => c.Name == "Otsumami · Small Bites");

            if (sampleCategory != null &&
                !await db.MenuItems.AnyAsync(m =>
                    m.CategoryId == sampleCategory.Id && m.Name == "Edamame"))
            {
                db.MenuItems.Add(new MenuItem
                {
                    CategoryId = sampleCategory.Id,
                    Name = "Edamame",
                    Description = "Lightly salted steamed soybeans.",
                    Price = 8.90m,
                    IsAvailable = true
                });
            }


        }

        private static async Task CreateUserAsync(UserManager<ApplicationUser> userManager,
            string email, string fullName, string role)
        {
            if (await userManager.FindByEmailAsync(email) != null) return;

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, DefaultPassword);
            if (result.Succeeded)
                await userManager.AddToRoleAsync(user, role);
        }
    }
}
