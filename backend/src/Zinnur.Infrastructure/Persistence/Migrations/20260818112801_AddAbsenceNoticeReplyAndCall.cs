using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAbsenceNoticeReplyAndCall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbsenceNotices_StudentId",
                table: "AbsenceNotices");

            migrationBuilder.AddColumn<string>(
                name: "CallNote",
                table: "AbsenceNotices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CalledAt",
                table: "AbsenceNotices",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CalledById",
                table: "AbsenceNotices",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RepliedAt",
                table: "AbsenceNotices",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplyText",
                table: "AbsenceNotices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceNotices_AwaitingReply",
                table: "AbsenceNotices",
                columns: new[] { "StudentId", "SentAt" },
                filter: " \"RepliedAt\" IS NULL ");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceNotices_CalledById",
                table: "AbsenceNotices",
                column: "CalledById");

            migrationBuilder.AddForeignKey(
                name: "FK_AbsenceNotices_Users_CalledById",
                table: "AbsenceNotices",
                column: "CalledById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AbsenceNotices_Users_CalledById",
                table: "AbsenceNotices");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceNotices_AwaitingReply",
                table: "AbsenceNotices");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceNotices_CalledById",
                table: "AbsenceNotices");

            migrationBuilder.DropColumn(
                name: "CallNote",
                table: "AbsenceNotices");

            migrationBuilder.DropColumn(
                name: "CalledAt",
                table: "AbsenceNotices");

            migrationBuilder.DropColumn(
                name: "CalledById",
                table: "AbsenceNotices");

            migrationBuilder.DropColumn(
                name: "RepliedAt",
                table: "AbsenceNotices");

            migrationBuilder.DropColumn(
                name: "ReplyText",
                table: "AbsenceNotices");

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceNotices_StudentId",
                table: "AbsenceNotices",
                column: "StudentId");
        }
    }
}
