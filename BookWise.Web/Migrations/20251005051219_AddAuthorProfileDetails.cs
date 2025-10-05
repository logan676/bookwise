using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorProfileDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DoubanAuthorId",
                table: "Authors",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoubanAuthorType",
                table: "Authors",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoubanProfileUrl",
                table: "Authors",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileBirthDate",
                table: "Authors",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileBirthPlace",
                table: "Authors",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileGender",
                table: "Authors",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileOccupation",
                table: "Authors",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileOtherNames",
                table: "Authors",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileWebsiteUrl",
                table: "Authors",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DoubanAuthorId",
                table: "Authors");

            migrationBuilder.DropColumn(
                name: "DoubanAuthorType",
                table: "Authors");

            migrationBuilder.DropColumn(
                name: "DoubanProfileUrl",
                table: "Authors");

            migrationBuilder.DropColumn(
                name: "ProfileBirthDate",
                table: "Authors");

            migrationBuilder.DropColumn(
                name: "ProfileBirthPlace",
                table: "Authors");

            migrationBuilder.DropColumn(
                name: "ProfileGender",
                table: "Authors");

            migrationBuilder.DropColumn(
                name: "ProfileOccupation",
                table: "Authors");

            migrationBuilder.DropColumn(
                name: "ProfileOtherNames",
                table: "Authors");

            migrationBuilder.DropColumn(
                name: "ProfileWebsiteUrl",
                table: "Authors");
        }
    }
}
