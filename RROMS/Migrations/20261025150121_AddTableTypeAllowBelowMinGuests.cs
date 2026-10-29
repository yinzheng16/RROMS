using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class AddTableTypeAllowBelowMinGuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowBelowMinGuests",
                table: "TableTypeDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowBelowMinGuests",
                table: "TableTypeDefinitions");
        }
    }
}
