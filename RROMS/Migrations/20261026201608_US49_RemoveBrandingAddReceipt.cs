using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class US49_RemoveBrandingAddReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandingMode",
                table: "RestaurantSettings");

            migrationBuilder.DropColumn(
                name: "FaviconPath",
                table: "RestaurantSettings");

            migrationBuilder.DropColumn(
                name: "FooterImagePath",
                table: "RestaurantSettings");

            migrationBuilder.DropColumn(
                name: "LogoPath",
                table: "RestaurantSettings");

            migrationBuilder.DropColumn(
                name: "NameLine1Size",
                table: "RestaurantSettings");

            migrationBuilder.DropColumn(
                name: "NameLine2Size",
                table: "RestaurantSettings");

            migrationBuilder.DropColumn(
                name: "RestaurantName",
                table: "RestaurantSettings");

            migrationBuilder.DropColumn(
                name: "RestaurantNameLine2",
                table: "RestaurantSettings");

            migrationBuilder.AddColumn<string>(
                name: "ReceiptFooterNote",
                table: "RestaurantSettings",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ServiceChargeOnTakeaway",
                table: "RestaurantSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SstRegistrationNo",
                table: "RestaurantSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptFooterNote",
                table: "RestaurantSettings");

            migrationBuilder.DropColumn(
                name: "ServiceChargeOnTakeaway",
                table: "RestaurantSettings");

            migrationBuilder.DropColumn(
                name: "SstRegistrationNo",
                table: "RestaurantSettings");

            migrationBuilder.AddColumn<string>(
                name: "BrandingMode",
                table: "RestaurantSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FaviconPath",
                table: "RestaurantSettings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FooterImagePath",
                table: "RestaurantSettings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoPath",
                table: "RestaurantSettings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameLine1Size",
                table: "RestaurantSettings",
                type: "nvarchar(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameLine2Size",
                table: "RestaurantSettings",
                type: "nvarchar(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RestaurantName",
                table: "RestaurantSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RestaurantNameLine2",
                table: "RestaurantSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);
        }
    }
}
