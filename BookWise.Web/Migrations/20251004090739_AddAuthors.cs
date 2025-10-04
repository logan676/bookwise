using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Books_Title_Author",
                table: "Books");

            migrationBuilder.AddColumn<int>(
                name: "AuthorId",
                table: "Books",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Authors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Authors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Authors_NormalizedName",
                table: "Authors",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.Sql(@"
                INSERT INTO Authors (Name, NormalizedName, CreatedAt)
                VALUES ('Unknown Author', 'unknown author', CURRENT_TIMESTAMP)
                ON CONFLICT(NormalizedName) DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO Authors (Name, NormalizedName, CreatedAt)
                SELECT DISTINCT author_name, lower(author_name), CURRENT_TIMESTAMP
                FROM (
                    SELECT TRIM(Author) AS author_name
                    FROM Books
                    WHERE Author IS NOT NULL AND TRIM(Author) <> ''
                )
                WHERE author_name <> '';
            ");

            migrationBuilder.Sql(@"
                UPDATE Books
                SET Author = TRIM(Author)
                WHERE Author IS NOT NULL;
            ");

            migrationBuilder.Sql(@"
                UPDATE Books
                SET AuthorId = (
                    SELECT Id FROM Authors
                    WHERE NormalizedName = lower(Author)
                    LIMIT 1
                )
                WHERE Author IS NOT NULL AND Author <> '';
            ");

            migrationBuilder.Sql(@"
                UPDATE Books
                SET Author = 'Unknown Author',
                    AuthorId = (
                        SELECT Id FROM Authors
                        WHERE NormalizedName = 'unknown author'
                        LIMIT 1
                    )
                WHERE AuthorId IS NULL OR AuthorId = 0;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Books_AuthorId",
                table: "Books",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_Title_AuthorId",
                table: "Books",
                columns: new[] { "Title", "AuthorId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Authors_AuthorId",
                table: "Books",
                column: "AuthorId",
                principalTable: "Authors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_Authors_AuthorId",
                table: "Books");

            migrationBuilder.DropTable(
                name: "Authors");

            migrationBuilder.DropIndex(
                name: "IX_Books_AuthorId",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Books_Title_AuthorId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "AuthorId",
                table: "Books");

            migrationBuilder.CreateIndex(
                name: "IX_Books_Title_Author",
                table: "Books",
                columns: new[] { "Title", "Author" });
        }
    }
}
