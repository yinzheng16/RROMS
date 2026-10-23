using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    public partial class SyncRestaurantSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LogoPath already exists in the current database.
            // This migration only synchronizes the EF Core migration state.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No database change to reverse.
        }
    }
}