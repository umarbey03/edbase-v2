using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfStudySubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CourseId = table.Column<long>(type: "bigint", nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionPlans_Months", "\"Months\" BETWEEN 1 AND 12");
                    table.CheckConstraint("CK_SubscriptionPlans_Price", "\"Price\" >= 0");
                    table.ForeignKey(
                        name: "FK_SubscriptionPlans_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPeriods",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    PlanId = table.Column<long>(type: "bigint", nullable: true),
                    CourseId = table.Column<long>(type: "bigint", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CancelledById = table.Column<long>(type: "bigint", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPeriods", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionPeriods_Amount", "\"Amount\" >= 0");
                    table.CheckConstraint("CK_SubscriptionPeriods_Range", "\"EndsAt\" > \"StartsAt\"");
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_SubscriptionPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_Users_CancelledById",
                        column: x => x.CancelledById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPeriods_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_CancelledById",
                table: "SubscriptionPeriods",
                column: "CancelledById");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_CourseId",
                table: "SubscriptionPeriods",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_CreatedById",
                table: "SubscriptionPeriods",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_PlanId",
                table: "SubscriptionPeriods",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPeriods_StudentId_EndsAt",
                table: "SubscriptionPeriods",
                columns: new[] { "StudentId", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_CourseId",
                table: "SubscriptionPlans",
                column: "CourseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionPeriods");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");
        }
    }
}
