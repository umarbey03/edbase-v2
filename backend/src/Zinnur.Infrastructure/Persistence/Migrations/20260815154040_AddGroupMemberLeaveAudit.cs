using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupMemberLeaveAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeftAt",
                table: "GroupMembers",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LeftById",
                table: "GroupMembers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MovedToGroupId",
                table: "GroupMembers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "GroupMembers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_LeftById",
                table: "GroupMembers",
                column: "LeftById");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_MovedToGroupId",
                table: "GroupMembers",
                column: "MovedToGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMembers_Groups_MovedToGroupId",
                table: "GroupMembers",
                column: "MovedToGroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMembers_Users_LeftById",
                table: "GroupMembers",
                column: "LeftById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupMembers_Groups_MovedToGroupId",
                table: "GroupMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_GroupMembers_Users_LeftById",
                table: "GroupMembers");

            migrationBuilder.DropIndex(
                name: "IX_GroupMembers_LeftById",
                table: "GroupMembers");

            migrationBuilder.DropIndex(
                name: "IX_GroupMembers_MovedToGroupId",
                table: "GroupMembers");

            migrationBuilder.DropColumn(
                name: "LeftAt",
                table: "GroupMembers");

            migrationBuilder.DropColumn(
                name: "LeftById",
                table: "GroupMembers");

            migrationBuilder.DropColumn(
                name: "MovedToGroupId",
                table: "GroupMembers");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "GroupMembers");
        }
    }
}
