using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandingMode",
                table: "RestaurantSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandingMode",
                table: "RestaurantSettings");
        }
    }
}
