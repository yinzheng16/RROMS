using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class AddCartDiningSessionSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiningSessionId",
                table: "CustomerCarts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCarts_DiningSessionId",
                table: "CustomerCarts",
                column: "DiningSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerCarts_DiningSessions_DiningSessionId",
                table: "CustomerCarts",
                column: "DiningSessionId",
                principalTable: "DiningSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerCarts_DiningSessions_DiningSessionId",
                table: "CustomerCarts");

            migrationBuilder.DropIndex(
                name: "IX_CustomerCarts_DiningSessionId",
                table: "CustomerCarts");

            migrationBuilder.DropColumn(
                name: "DiningSessionId",
                table: "CustomerCarts");
        }
    }
}
