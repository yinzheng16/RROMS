using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;

namespace RROMS.Data
{
    // Shared database context for the whole system (data access tier).
    // To add a table for your module: create the class in Models/, add a DbSet below,
    // then run Add-Migration <Name> after pulling the latest develop branch.
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Member 2: Tables & Reservations
        public DbSet<DiningArea> DiningAreas => Set<DiningArea>();
        public DbSet<RestaurantTable> RestaurantTables => Set<RestaurantTable>();
        public DbSet<TableTypeDefinition> TableTypeDefinitions => Set<TableTypeDefinition>();
        public DbSet<OperatingHour> OperatingHours => Set<OperatingHour>();
        public DbSet<SpecialDate> SpecialDates => Set<SpecialDate>();
        public DbSet<Reservation> Reservations => Set<Reservation>();

        // Member 3: Menu & Orders
        public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
        public DbSet<MenuItem> MenuItems => Set<MenuItem>();
        public DbSet<MenuCodeSequence> MenuCodeSequences => Set<MenuCodeSequence>();
        public DbSet<Promotion> Promotions => Set<Promotion>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<CustomerCart> CustomerCarts => Set<CustomerCart>();
        public DbSet<CustomerCartLine> CustomerCartLines => Set<CustomerCartLine>();

        // Member 4: Payments & Notifications
        public DbSet<Bill> Bills => Set<Bill>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Notification> Notifications => Set<Notification>();

        // Member 5: Feedback & Settings
        public DbSet<Feedback> Feedbacks => Set<Feedback>();
        public DbSet<RestaurantSetting> RestaurantSettings => Set<RestaurantSetting>();

        // Member 1: Chatbot
        public DbSet<ChatFaq> ChatFaqs => Set<ChatFaq>();

        // Save every enum as its name (e.g. "Paid") instead of a number,
        // so tables are readable in SSMS. Add a line here for each new enum.
        protected override void ConfigureConventions(ModelConfigurationBuilder builder)
        {
            builder.Properties<ReservationStatus>().HaveConversion<string>().HaveMaxLength(20);
            builder.Properties<OrderType>().HaveConversion<string>().HaveMaxLength(20);
            builder.Properties<OrderStatus>().HaveConversion<string>().HaveMaxLength(20);
            builder.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(20);
            builder.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(20);
            builder.Properties<BillStatus>().HaveConversion<string>().HaveMaxLength(20);
            builder.Properties<NotificationType>().HaveConversion<string>().HaveMaxLength(20);
            builder.Properties<DayOfWeek>().HaveConversion<string>().HaveMaxLength(10);
            builder.Properties<TableType>().HaveConversion<string>().HaveMaxLength(20);
            builder.Properties<TableStatus>().HaveConversion<string>().HaveMaxLength(20);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            // Must stay first: creates the Identity tables (AspNetUsers, AspNetRoles, ...).
            base.OnModelCreating(builder);

            // ---------- Tables & Reservations ----------
            builder.Entity<TableTypeDefinition>(e =>
            {
                e.HasIndex(t => t.Name).IsUnique();

                e.HasIndex(t => t.Code)
                    .IsUnique()
                    .HasFilter("[Code] IS NOT NULL");

                e.HasOne(t => t.DefaultDiningArea)
                    .WithMany()
                    .HasForeignKey(t => t.DefaultDiningAreaId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            builder.Entity<RestaurantTable>(e =>
            {
                e.HasIndex(t => t.TableNumber).IsUnique();
                e.HasOne(t => t.TableTypeDefinition)
                     .WithMany(t => t.Tables)
                     .HasForeignKey(t => t.TableTypeDefinitionId)
                     .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(t => t.DiningArea)
                     .WithMany(a => a.Tables)
                     .HasForeignKey(t => t.DiningAreaId)
                     .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<OperatingHour>()
                .HasIndex(h => h.Day).IsUnique();

            builder.Entity<SpecialDate>(e =>
            {
                e.HasIndex(s => s.Date).IsUnique();
                e.Property(s => s.Name).HasMaxLength(100).IsRequired();
                e.Property(s => s.Date).HasColumnType("date");
            });

            builder.Entity<Reservation>(e =>
            {
                e.HasIndex(r => r.ReferenceNo).IsUnique();
                e.HasIndex(r => new { r.TableId, r.ReservedAt });
                e.HasOne(r => r.User)
                 .WithMany(u => u.Reservations)
                 .HasForeignKey(r => r.UserId)
                 .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(r => r.Table)
                 .WithMany(t => t.Reservations)
                 .HasForeignKey(r => r.TableId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- Menu & Orders ----------
            builder.Entity<CustomerCart>(e =>
            {
                e.HasKey(c => c.UserId);
                e.Property(c => c.Revision).IsConcurrencyToken();
                e.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<CustomerCartLine>(e =>
            {
                e.Property(l => l.Id).ValueGeneratedNever();
                e.HasOne(l => l.Cart).WithMany(c => c.Lines).HasForeignKey(l => l.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(l => l.MenuItem).WithMany().HasForeignKey(l => l.MenuItemId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<MenuCategory>(e =>
            {
                e.HasIndex(c => c.Name).IsUnique();
                e.HasIndex(c => c.Code).IsUnique();
            });

            builder.Entity<MenuItem>(e =>
            {
                e.Property(m => m.Price).HasPrecision(10, 2);
                e.HasIndex(m => m.Code).IsUnique();
                e.HasOne(m => m.Category)
                 .WithMany(c => c.MenuItems)
                 .HasForeignKey(m => m.CategoryId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Promotion>(e =>
            {
                e.Property(p => p.DiscountPercent).HasPrecision(5, 2);
                e.HasOne(p => p.MenuItem)
                 .WithMany(m => m.Promotions)
                 .HasForeignKey(p => p.MenuItemId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<Order>(e =>
            {
                e.HasIndex(o => o.OrderNo).IsUnique();
                // US32: Improve lookup performance for Orders linked to a Bill.
                e.HasIndex(o => o.BillId);
                e.Property(o => o.Subtotal).HasPrecision(10, 2);
                e.Property(o => o.TaxAmount).HasPrecision(10, 2);
                e.Property(o => o.ServiceCharge).HasPrecision(10, 2);
                e.Property(o => o.TotalAmount).HasPrecision(10, 2);
                e.HasOne(o => o.User)
                 .WithMany(u => u.Orders)
                 .HasForeignKey(o => o.UserId)
                 .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(o => o.Reservation)
                 .WithMany(r => r.Orders)
                 .HasForeignKey(o => o.ReservationId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<OrderItem>(e =>
            {
                e.Property(i => i.UnitPrice).HasPrecision(10, 2);
                e.HasOne(i => i.Order)
                 .WithMany(o => o.Items)
                 .HasForeignKey(i => i.OrderId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(i => i.MenuItem)
                 .WithMany()
                 .HasForeignKey(i => i.MenuItemId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ---------- Payments & Notifications ----------
            builder.Entity<Bill>(e =>
            {
                e.Property(b => b.Subtotal).HasPrecision(10, 2);
                e.Property(b => b.TaxAmount).HasPrecision(10, 2);
                e.Property(b => b.ServiceCharge).HasPrecision(10, 2);
                e.Property(b => b.TotalAmount).HasPrecision(10, 2);

                e.Property(b => b.Status)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                e.HasMany(b => b.Orders)
                    .WithOne(o => o.Bill)
                    .HasForeignKey(o => o.BillId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasMany(b => b.Payments)
                    .WithOne(p => p.Bill)
                    .HasForeignKey(p => p.BillId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Payment>(e =>
            {
                e.Property(p => p.Amount).HasPrecision(10, 2);
                e.HasOne(p => p.Order)
                 .WithMany(o => o.Payments)
                 .HasForeignKey(p => p.OrderId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Notification>(e =>
            {
                e.HasIndex(n => new { n.UserId, n.IsRead });
                e.HasOne(n => n.User)
                 .WithMany(u => u.Notifications)
                 .HasForeignKey(n => n.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ---------- Feedback & Settings ----------
            builder.Entity<Feedback>(e =>
            {
                e.HasIndex(f => f.OrderId).IsUnique();   // one review per order
                e.HasOne(f => f.Order)
                 .WithOne(o => o.Feedback)
                 .HasForeignKey<Feedback>(f => f.OrderId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<RestaurantSetting>(e =>
            {
                e.Property(s => s.TaxRatePercent).HasPrecision(5, 2);
                e.Property(s => s.ServiceChargePercent).HasPrecision(5, 2);
            });
        }
    }
}
