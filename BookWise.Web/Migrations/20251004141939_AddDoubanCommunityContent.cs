using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDoubanCommunityContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DoubanSubjectId",
                table: "Books",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "BookQuotes",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Snapshot");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DoubanSubjectId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "BookQuotes");
        }
    }
}
