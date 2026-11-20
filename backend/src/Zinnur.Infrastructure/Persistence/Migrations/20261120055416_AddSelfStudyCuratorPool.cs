using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfStudyCuratorPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SelfStudyCuratorPoolMembers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CuratorId = table.Column<long>(type: "bigint", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    IsAccepting = table.Column<bool>(type: "boolean", nullable: false),
                    AddedById = table.Column<long>(type: "bigint", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfStudyCuratorPoolMembers", x => x.Id);
                    table.CheckConstraint("CK_SelfStudyCuratorPoolMembers_Capacity", "\"Capacity\" BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "FK_SelfStudyCuratorPoolMembers_Users_AddedById",
                        column: x => x.AddedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelfStudyCuratorPoolMembers_Users_CuratorId",
                        column: x => x.CuratorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SelfStudyCuratorPoolMembers_AddedById",
                table: "SelfStudyCuratorPoolMembers",
                column: "AddedById");

            migrationBuilder.CreateIndex(
                name: "IX_SelfStudyCuratorPoolMembers_CuratorId",
                table: "SelfStudyCuratorPoolMembers",
                column: "CuratorId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SelfStudyCuratorPoolMembers");
        }
    }
}
