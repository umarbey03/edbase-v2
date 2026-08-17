using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupMembershipEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupMembershipEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    GroupId = table.Column<long>(type: "bigint", nullable: false),
                    TeacherId = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MovedToGroupId = table.Column<long>(type: "bigint", nullable: true),
                    ActorId = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    LessonsCompleted = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembershipEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupMembershipEvents_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupMembershipEvents_Groups_MovedToGroupId",
                        column: x => x.MovedToGroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupMembershipEvents_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupMembershipEvents_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroupMembershipEvents_Users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembershipEvents_ActorId",
                table: "GroupMembershipEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembershipEvents_GroupId_Kind",
                table: "GroupMembershipEvents",
                columns: new[] { "GroupId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembershipEvents_MovedToGroupId",
                table: "GroupMembershipEvents",
                column: "MovedToGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembershipEvents_OccurredAt",
                table: "GroupMembershipEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembershipEvents_StudentId_OccurredAt",
                table: "GroupMembershipEvents",
                columns: new[] { "StudentId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembershipEvents_TeacherId",
                table: "GroupMembershipEvents",
                column: "TeacherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupMembershipEvents");
        }
    }
}
