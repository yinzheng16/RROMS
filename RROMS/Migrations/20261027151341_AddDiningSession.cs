using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class AddDiningSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiningSessionId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiningSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TableId = table.Column<int>(type: "int", nullable: false),
                    ReservationId = table.Column<int>(type: "int", nullable: true),
                    CustomerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiningSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiningSessions_AspNetUsers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiningSessions_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiningSessions_RestaurantTables_TableId",
                        column: x => x.TableId,
                        principalTable: "RestaurantTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DiningSessionId",
                table: "Orders",
                column: "DiningSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DiningSessions_CustomerId",
                table: "DiningSessions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DiningSessions_ReservationId",
                table: "DiningSessions",
                column: "ReservationId",
                unique: true,
                filter: "[ReservationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DiningSessions_TableId",
                table: "DiningSessions",
                column: "TableId",
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_DiningSessions_TableId_Status",
                table: "DiningSessions",
                columns: new[] { "TableId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_DiningSessions_DiningSessionId",
                table: "Orders",
                column: "DiningSessionId",
                principalTable: "DiningSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_DiningSessions_DiningSessionId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "DiningSessions");

            migrationBuilder.DropIndex(
                name: "IX_Orders_DiningSessionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DiningSessionId",
                table: "Orders");
        }
    }
}
