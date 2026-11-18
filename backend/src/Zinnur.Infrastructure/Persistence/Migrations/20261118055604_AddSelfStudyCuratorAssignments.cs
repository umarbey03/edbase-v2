using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfStudyCuratorAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SelfStudyCuratorAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    CuratorId = table.Column<long>(type: "bigint", nullable: false),
                    BackupCuratorId = table.Column<long>(type: "bigint", nullable: true),
                    AssignedById = table.Column<long>(type: "bigint", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    EndedById = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfStudyCuratorAssignments", x => x.Id);
                    table.CheckConstraint("CK_SelfStudyCuratorAssignments_BackupDiffers", "\"BackupCuratorId\" IS NULL OR \"BackupCuratorId\" <> \"CuratorId\"");
                    table.CheckConstraint("CK_SelfStudyCuratorAssignments_NotSelf", "\"StudentId\" <> \"CuratorId\"");
                    table.ForeignKey(
                        name: "FK_SelfStudyCuratorAssignments_Users_AssignedById",
                        column: x => x.AssignedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelfStudyCuratorAssignments_Users_BackupCuratorId",
                        column: x => x.BackupCuratorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelfStudyCuratorAssignments_Users_CuratorId",
                        column: x => x.CuratorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelfStudyCuratorAssignments_Users_EndedById",
                        column: x => x.EndedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelfStudyCuratorAssignments_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SelfStudyCuratorAssignments_AssignedById",
                table: "SelfStudyCuratorAssignments",
                column: "AssignedById");

            migrationBuilder.CreateIndex(
                name: "IX_SelfStudyCuratorAssignments_BackupCuratorId_EndedAt",
                table: "SelfStudyCuratorAssignments",
                columns: new[] { "BackupCuratorId", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SelfStudyCuratorAssignments_CuratorId_EndedAt",
                table: "SelfStudyCuratorAssignments",
                columns: new[] { "CuratorId", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SelfStudyCuratorAssignments_EndedById",
                table: "SelfStudyCuratorAssignments",
                column: "EndedById");

            migrationBuilder.CreateIndex(
                name: "UX_SelfStudyCuratorAssignments_ActiveStudent",
                table: "SelfStudyCuratorAssignments",
                column: "StudentId",
                unique: true,
                filter: " \"EndedAt\" IS NULL ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SelfStudyCuratorAssignments");
        }
    }
}
