using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisCriteriaAndSessionReviewScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisCriteria",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisCriteria", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionReviewScores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionReviewId = table.Column<long>(type: "bigint", nullable: false),
                    CriterionId = table.Column<long>(type: "bigint", nullable: true),
                    CriterionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    Score = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionReviewScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionReviewScores_AnalysisCriteria_CriterionId",
                        column: x => x.CriterionId,
                        principalTable: "AnalysisCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SessionReviewScores_SessionReviews_SessionReviewId",
                        column: x => x.SessionReviewId,
                        principalTable: "SessionReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisCriteria_SortOrder",
                table: "AnalysisCriteria",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_SessionReviewScores_CriterionId",
                table: "SessionReviewScores",
                column: "CriterionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionReviewScores_SessionReviewId",
                table: "SessionReviewScores",
                column: "SessionReviewId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionReviewScores");

            migrationBuilder.DropTable(
                name: "AnalysisCriteria");
        }
    }
}
