using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceExcusedFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExcuseReason",
                table: "Attendances",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExcused",
                table: "Attendances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NewIsExcused",
                table: "AttendanceAudits",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OldIsExcused",
                table: "AttendanceAudits",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcuseReason",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "IsExcused",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "NewIsExcused",
                table: "AttendanceAudits");

            migrationBuilder.DropColumn(
                name: "OldIsExcused",
                table: "AttendanceAudits");
        }
    }
}
