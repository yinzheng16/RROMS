using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class AddTableTypeMinSpendPhotoStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "RestaurantTables",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinGuests",
                table: "RestaurantTables",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumSpend",
                table: "RestaurantTables",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PhotoPath",
                table: "RestaurantTables",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "RestaurantTables",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Available");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "RestaurantTables",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "MinGuests",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "MinimumSpend",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "PhotoPath",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "RestaurantTables");
        }
    }
}
