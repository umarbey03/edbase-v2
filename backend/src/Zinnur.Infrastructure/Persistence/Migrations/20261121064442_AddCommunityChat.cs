using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommunityMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AuthorId = table.Column<long>(type: "bigint", nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ReplyToId = table.Column<long>(type: "bigint", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    DeletedById = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommunityMessages_CommunityMessages_ReplyToId",
                        column: x => x.ReplyToId,
                        principalTable: "CommunityMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CommunityMessages_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityMessages_Users_DeletedById",
                        column: x => x.DeletedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CommunityMutes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    MutedUntil = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MutedById = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityMutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommunityMutes_Users_MutedById",
                        column: x => x.MutedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommunityMutes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommunityReadStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    LastReadMessageId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityReadStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommunityReadStates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityMessages_AuthorId_CreatedAt",
                table: "CommunityMessages",
                columns: new[] { "AuthorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityMessages_CreatedAt",
                table: "CommunityMessages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityMessages_DeletedById",
                table: "CommunityMessages",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityMessages_ReplyToId",
                table: "CommunityMessages",
                column: "ReplyToId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityMutes_MutedById",
                table: "CommunityMutes",
                column: "MutedById");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityMutes_UserId",
                table: "CommunityMutes",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunityReadStates_UserId",
                table: "CommunityReadStates",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommunityMessages");

            migrationBuilder.DropTable(
                name: "CommunityMutes");

            migrationBuilder.DropTable(
                name: "CommunityReadStates");
        }
    }
}
