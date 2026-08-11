using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wave1_Group_VideoStartLesson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "VideoStartLessonId",
                table: "Groups",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_VideoStartLessonId",
                table: "Groups",
                column: "VideoStartLessonId");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_ModuleLessons_VideoStartLessonId",
                table: "Groups",
                column: "VideoStartLessonId",
                principalTable: "ModuleLessons",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_ModuleLessons_VideoStartLessonId",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_VideoStartLessonId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "VideoStartLessonId",
                table: "Groups");
        }
    }
}
