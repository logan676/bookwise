using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBookQuoteAndMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Quote",
                table: "Books",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Quote",
                table: "Books");
        }
    }
}
