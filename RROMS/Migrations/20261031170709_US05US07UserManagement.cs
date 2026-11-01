using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class US05US07UserManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserAdminLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TargetUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TargetName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DoneByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DoneByName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DoneAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAdminLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAdminLogs_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAdminLogs_DoneAt",
                table: "UserAdminLogs",
                column: "DoneAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserAdminLogs_TargetUserId_Action",
                table: "UserAdminLogs",
                columns: new[] { "TargetUserId", "Action" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAdminLogs");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "AspNetUsers");
        }
    }
}
