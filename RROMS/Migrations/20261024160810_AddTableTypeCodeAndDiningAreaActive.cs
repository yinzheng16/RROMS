using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class AddTableTypeCodeAndDiningAreaActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "TableTypeDefinitions",
                type: "nvarchar(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "DiningAreas",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_TableTypeDefinitions_Code",
                table: "TableTypeDefinitions",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TableTypeDefinitions_Code",
                table: "TableTypeDefinitions");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "TableTypeDefinitions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "DiningAreas");
        }
    }
}
