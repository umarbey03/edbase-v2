using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitSessionReviewBodyIntoPlusMinusConclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Body",
                table: "SessionReviews");

            migrationBuilder.AddColumn<string>(
                name: "Conclusion",
                table: "SessionReviews",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Minus",
                table: "SessionReviews",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plus",
                table: "SessionReviews",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Conclusion",
                table: "SessionReviews");

            migrationBuilder.DropColumn(
                name: "Minus",
                table: "SessionReviews");

            migrationBuilder.DropColumn(
                name: "Plus",
                table: "SessionReviews");

            migrationBuilder.AddColumn<string>(
                name: "Body",
                table: "SessionReviews",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");
        }
    }
}
