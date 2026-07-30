using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupScheduleRuleAndMemberPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CourseMonths",
                table: "Groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "CuratorGroupId",
                table: "Groups",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "Groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "Groups",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int[]>(
                name: "Weekdays",
                table: "Groups",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PausedUntil",
                table: "GroupMembers",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_CuratorGroupId",
                table: "Groups",
                column: "CuratorGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_Groups_CuratorGroupId",
                table: "Groups",
                column: "CuratorGroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_Groups_CuratorGroupId",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_CuratorGroupId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "CourseMonths",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "CuratorGroupId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "Weekdays",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "PausedUntil",
                table: "GroupMembers");
        }
    }
}
