using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthorRecommendations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FocusAuthor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RecommendedAuthor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ConfidenceScore = table.Column<decimal>(type: "TEXT", precision: 3, scale: 2, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorRecommendations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorRecommendations_FocusAuthor_RecommendedAuthor",
                table: "AuthorRecommendations",
                columns: new[] { "FocusAuthor", "RecommendedAuthor" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthorRecommendations");
        }
    }
}
