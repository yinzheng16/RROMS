using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicTableTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TableTypeDefinitionId",
                table: "RestaurantTables",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TableTypeDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StartingNumber = table.Column<int>(type: "int", nullable: false),
                    NumberPadding = table.Column<int>(type: "int", nullable: false),
                    DefaultDiningAreaId = table.Column<int>(type: "int", nullable: false),
                    DefaultMinGuests = table.Column<int>(type: "int", nullable: false),
                    DefaultMaxGuests = table.Column<int>(type: "int", nullable: false),
                    DefaultMinimumSpend = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PhotoPath = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableTypeDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TableTypeDefinitions_DiningAreas_DefaultDiningAreaId",
                        column: x => x.DefaultDiningAreaId,
                        principalTable: "DiningAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantTables_TableTypeDefinitionId",
                table: "RestaurantTables",
                column: "TableTypeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_TableTypeDefinitions_DefaultDiningAreaId",
                table: "TableTypeDefinitions",
                column: "DefaultDiningAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_TableTypeDefinitions_Name",
                table: "TableTypeDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RestaurantTables_TableTypeDefinitions_TableTypeDefinitionId",
                table: "RestaurantTables",
                column: "TableTypeDefinitionId",
                principalTable: "TableTypeDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                INSERT INTO TableTypeDefinitions
                    (Name, Prefix, StartingNumber, NumberPadding, DefaultDiningAreaId, DefaultMinGuests, DefaultMaxGuests, DefaultMinimumSpend, PhotoPath, IsActive)
                SELECT
                    v.Name, v.Prefix, 1, 1, a.Id, v.MinGuests, v.MaxGuests, v.MinimumSpend, v.PhotoPath, 1
                FROM (VALUES
                    ('Counter seat', 'C', 'Bar Counter', 1, 1, 0, '/images/tables/counter.jpg'),
                    ('Table for two', 'D', 'Main Hall', 1, 2, 0, '/images/tables/double.jpg'),
                    ('Standard table', 'T', 'Main Hall', 3, 6, 0, '/images/tables/standard.jpg'),
                    ('VIP room', 'VIP', 'Private Rooms', 1, 10, 500, '/images/tables/vip.jpg'),
                    ('VVIP room', 'VVIP', 'Private Rooms', 1, 20, 1000, '/images/tables/vvip.jpg')
                ) AS v(Name, Prefix, AreaName, MinGuests, MaxGuests, MinimumSpend, PhotoPath)
                INNER JOIN DiningAreas a ON a.Name = v.AreaName
                WHERE NOT EXISTS ( SELECT 1 FROM TableTypeDefinitions d WHERE d.Name = v.Name );

                UPDATE t
                SET TableTypeDefinitionId = d.Id
                FROM RestaurantTables t
                INNER JOIN TableTypeDefinitions d
                    ON d.Name = CASE t.Type
                        WHEN 'Counter' THEN 'Counter seat'
                        WHEN 'Double' THEN 'Table for two'
                        WHEN 'Standard' THEN 'Standard table'
                        WHEN 'Vip' THEN 'VIP room'
                        WHEN 'Vvip' THEN 'VVIP room'
                    END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RestaurantTables_TableTypeDefinitions_TableTypeDefinitionId",
                table: "RestaurantTables");

            migrationBuilder.DropTable(
                name: "TableTypeDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_RestaurantTables_TableTypeDefinitionId",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "TableTypeDefinitionId",
                table: "RestaurantTables");
        }
    }
}
