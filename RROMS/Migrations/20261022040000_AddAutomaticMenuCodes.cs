using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RROMS.Migrations
{
    // Adds stored display codes without rebuilding menu records or their IDs.
    public partial class AddAutomaticMenuCodes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "Code", table: "MenuCategories",
                type: "nvarchar(12)", maxLength: 12, nullable: true);
            migrationBuilder.AddColumn<string>(name: "Code", table: "MenuItems",
                type: "nvarchar(24)", maxLength: 24, nullable: true);
            migrationBuilder.CreateTable(name: "MenuCodeSequences",
                columns: table => new
                {
                    Prefix = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    LastItemNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_MenuCodeSequences", x => x.Prefix));

            migrationBuilder.Sql("""
            DECLARE @Known TABLE ([CategoryKey] nvarchar(100) PRIMARY KEY, [Prefix] nvarchar(12));
            INSERT INTO @Known VALUES
                (N'OTSUMAMI', N'OT'),
                (N'SMALLBITES', N'OT'),
                (N'SARADA', N'SA'),
                (N'SALADS', N'SA'),
                (N'SASHIMI', N'SS'),
                (N'RAWSEAFOOD', N'SS'),
                (N'SUSHI', N'SU'),
                (N'MAKIMONO', N'MA'),
                (N'SUSHIROLLS', N'MA'),
                (N'YAKITORI', N'YT'),
                (N'CHICKENSKEWERS', N'YT'),
                (N'YAKIMONO', N'YM'),
                (N'GRILLEDDISHES', N'YM'),
                (N'AGEMONO', N'AG'),
                (N'FRIEDDISHES', N'AG'),
                (N'TEMPURA', N'TE'),
                (N'DONMONO', N'DO'),
                (N'DONBURI', N'DO'),
                (N'RICEBOWLS', N'DO'),
                (N'GOHAN', N'GO'),
                (N'RICEDISHES', N'GO'),
                (N'MENMONO', N'ME'),
                (N'NOODLES', N'ME'),
                (N'WANMONO', N'WA'),
                (N'SOUPSSTEAMEDDISHES', N'WA'),
                (N'KANMI', N'KA'),
                (N'DESSERTS', N'KA'),
                (N'ARUKORU', N'AL'),
                (N'ALCOHOLICDRINKS', N'AL'),
                (N'SOFUTODORINKU', N'SD'),
                (N'NONALCOHOLICDRINKS', N'SD');
            DECLARE @Alphabet nvarchar(26) = N'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
            DECLARE @Id int, @Name nvarchar(50), @Primary nvarchar(50), @Key nvarchar(100),
                @Letters nvarchar(100), @Code nvarchar(12), @Candidate nvarchar(12),
                @First nchar(1), @Second nchar(1), @Character nchar(1),
                @I int, @J int, @K int, @Position int, @Fallback int, @Value int, @Suffix nvarchar(10);
            DECLARE categories CURSOR LOCAL FAST_FORWARD FOR
                SELECT [Id], [Name] FROM [MenuCategories] ORDER BY [Id];
            OPEN categories;
            FETCH NEXT FROM categories INTO @Id, @Name;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @Primary = LEFT(@Name, CHARINDEX(N'·', @Name + N'·') - 1);
                SET @Key = N'';
                SET @Letters = N'';
                SET @I = 1;
                WHILE @I <= LEN(@Primary)
                BEGIN
                    SET @Character = UPPER(SUBSTRING(@Primary, @I, 1));
                    -- Accent-insensitive Latin matching recognises Arukōru.
                    SET @Position = CHARINDEX(@Character COLLATE Latin1_General_100_CI_AI,
                        @Alphabet COLLATE Latin1_General_100_CI_AI);
                    IF UNICODE(@Character) NOT BETWEEN 768 AND 879 AND @Position BETWEEN 1 AND 26
                    BEGIN
                        SET @Key += SUBSTRING(@Alphabet, @Position, 1);
                        SET @Letters += SUBSTRING(@Alphabet, @Position, 1);
                    END
                    ELSE IF @Character COLLATE Latin1_General_100_BIN2 LIKE N'[0-9]'
                        SET @Key += @Character;
                    SET @I += 1;
                END;
                SET @Code = NULL;
                SELECT @Code = k.[Prefix] FROM @Known k
                    WHERE k.[CategoryKey] = @Key COLLATE Latin1_General_100_CI_AI
                      AND NOT EXISTS (SELECT 1 FROM [MenuCodeSequences] s WHERE s.[Prefix] = k.[Prefix]);
                SET @First = CASE WHEN LEN(@Letters) = 0 THEN N'C' ELSE LEFT(@Letters, 1) END;
                SET @I = 2;
                WHILE @Code IS NULL AND @I <= LEN(@Letters) + 1
                BEGIN
                    SET @Second = CASE WHEN @I <= LEN(@Letters) THEN SUBSTRING(@Letters, @I, 1) ELSE N'X' END;
                    SET @Candidate = @First + @Second;
                    IF NOT EXISTS (SELECT 1 FROM [MenuCodeSequences] WHERE [Prefix] = @Candidate)
                       AND NOT EXISTS (SELECT 1 FROM @Known WHERE [Prefix] = @Candidate)
                        SET @Code = @Candidate;
                    SET @I += 1;
                END;
                -- An empty/non-Latin name still receives the same CX candidate.
                IF @Code IS NULL AND LEN(@Letters) = 0
                   AND NOT EXISTS (SELECT 1 FROM [MenuCodeSequences] WHERE [Prefix] = N'CX')
                    SET @Code = N'CX';
                SET @J = 1;
                WHILE @Code IS NULL AND @J <= 26
                BEGIN
                    SET @K = 1;
                    WHILE @Code IS NULL AND @K <= 26
                    BEGIN
                        SET @Candidate = SUBSTRING(@Alphabet, @J, 1) + SUBSTRING(@Alphabet, @K, 1);
                        IF NOT EXISTS (SELECT 1 FROM [MenuCodeSequences] WHERE [Prefix] = @Candidate)
                           AND NOT EXISTS (SELECT 1 FROM @Known WHERE [Prefix] = @Candidate)
                            SET @Code = @Candidate;
                        SET @K += 1;
                    END;
                    SET @J += 1;
                END;
                SET @Fallback = 1;
                WHILE @Code IS NULL
                BEGIN
                    SET @Value = @Fallback;
                    SET @Suffix = N'';
                    WHILE @Value > 0
                    BEGIN
                        SET @Value -= 1;
                        SET @Suffix = SUBSTRING(@Alphabet, (@Value % 26) + 1, 1) + @Suffix;
                        SET @Value = @Value / 26;
                    END;
                    SET @Candidate = N'MC' + @Suffix;
                    IF NOT EXISTS (SELECT 1 FROM [MenuCodeSequences] WHERE [Prefix] = @Candidate)
                        SET @Code = @Candidate;
                    ELSE SET @Fallback += 1;
                END;
                INSERT INTO [MenuCodeSequences] ([Prefix], [LastItemNumber]) VALUES (@Code, 0);
                UPDATE [MenuCategories] SET [Code] = @Code WHERE [Id] = @Id;
                FETCH NEXT FROM categories INTO @Id, @Name;
            END;
            CLOSE categories;
            DEALLOCATE categories;

            ;WITH Numbered AS
            (
                SELECT [Id], [CategoryId], ROW_NUMBER() OVER (PARTITION BY [CategoryId] ORDER BY [Id]) AS [Number]
                FROM [MenuItems]
            )
            UPDATE item SET [Code] = category.[Code]
                + CASE WHEN n.[Number] < 10 THEN N'0' ELSE N'' END
                + CONVERT(nvarchar(10), n.[Number])
            FROM [MenuItems] item
            JOIN Numbered n ON n.[Id] = item.[Id]
            JOIN [MenuCategories] category ON category.[Id] = n.[CategoryId];

            UPDATE seq SET [LastItemNumber] = CONVERT(int, totals.[Count])
            FROM [MenuCodeSequences] seq
            JOIN [MenuCategories] category ON category.[Code] = seq.[Prefix]
            JOIN (SELECT [CategoryId], COUNT_BIG(*) AS [Count] FROM [MenuItems] GROUP BY [CategoryId]) totals
                ON totals.[CategoryId] = category.[Id];
            """);

            migrationBuilder.AlterColumn<string>(name: "Code", table: "MenuCategories",
                type: "nvarchar(12)", maxLength: 12, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(12)", oldMaxLength: 12, oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "Code", table: "MenuItems",
                type: "nvarchar(24)", maxLength: 24, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(24)", oldMaxLength: 24, oldNullable: true);
            migrationBuilder.CreateIndex(name: "IX_MenuCategories_Code", table: "MenuCategories", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_MenuItems_Code", table: "MenuItems", column: "Code", unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_MenuItems_Code", table: "MenuItems");
            migrationBuilder.DropIndex(name: "IX_MenuCategories_Code", table: "MenuCategories");
            migrationBuilder.DropColumn(name: "Code", table: "MenuItems");
            migrationBuilder.DropColumn(name: "Code", table: "MenuCategories");
            migrationBuilder.DropTable(name: "MenuCodeSequences");
        }
    }
}
