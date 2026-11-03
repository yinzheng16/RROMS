using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class AreaOccupiedAndTableStatusMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManualStatus",
                table: "DiningAreas");

            migrationBuilder.DropColumn(
                name: "StatusMode",
                table: "DiningAreas");

            migrationBuilder.AddColumn<string>(
                name: "StatusBeforeAreaOccupied",
                table: "RestaurantTables",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOccupied",
                table: "DiningAreas",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusBeforeAreaOccupied",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "IsOccupied",
                table: "DiningAreas");

            migrationBuilder.AddColumn<int>(
                name: "ManualStatus",
                table: "DiningAreas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusMode",
                table: "DiningAreas",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
