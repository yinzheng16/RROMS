using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JapaneseName",
                table: "TableTypeDefinitions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumSpendAccepted",
                table: "Reservations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MinimumSpendAcceptedAt",
                table: "Reservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionToken",
                table: "Reservations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JapaneseName",
                table: "TableTypeDefinitions");

            migrationBuilder.DropColumn(
                name: "MinimumSpendAccepted",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "MinimumSpendAcceptedAt",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "SubmissionToken",
                table: "Reservations");
        }
    }
}
