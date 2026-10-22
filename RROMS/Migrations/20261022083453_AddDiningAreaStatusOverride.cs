using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class AddDiningAreaStatusOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManualStatus",
                table: "DiningAreas");

            migrationBuilder.DropColumn(
                name: "StatusMode",
                table: "DiningAreas");
        }
    }
}
