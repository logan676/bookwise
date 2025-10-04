using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWise.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfileNotableWorks",
                table: "Authors",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProfileRefreshedAt",
                table: "Authors",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileSummary",
                table: "Authors",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileNotableWorks",
                table: "Authors");

            migrationBuilder.DropColumn(
                name: "ProfileRefreshedAt",
                table: "Authors");

            migrationBuilder.DropColumn(
                name: "ProfileSummary",
                table: "Authors");
        }
    }
}
