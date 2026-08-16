using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TeacherRates_Rates_NonNegative",
                table: "TeacherRates");

            migrationBuilder.AddColumn<decimal>(
                name: "ActiveStudentBonusRate",
                table: "TeacherRates",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseSalary",
                table: "TeacherRates",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WeekendHolidayMultiplier",
                table: "TeacherRates",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PremiumMultiplierApplied",
                table: "SessionPayouts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.CreateTable(
                name: "PayrollAdjustments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedById = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollAdjustments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollApprovals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SnapshotTotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovedById = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    PaidById = table.Column<long>(type: "bigint", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollApprovals_Users_ApprovedById",
                        column: x => x.ApprovedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollApprovals_Users_PaidById",
                        column: x => x.PaidById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollApprovals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TeacherRates_Multiplier",
                table: "TeacherRates",
                sql: "(\"WeekendHolidayMultiplier\" IS NULL OR \"WeekendHolidayMultiplier\" >= 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TeacherRates_Rates_NonNegative",
                table: "TeacherRates",
                sql: "(\"PerSessionRate\" >= 0 AND \"PerStudentBonusRate\" >= 0 AND \"BaseSalary\" >= 0 AND \"ActiveStudentBonusRate\" >= 0)");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_CreatedById",
                table: "PayrollAdjustments",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_UserId_PeriodStart",
                table: "PayrollAdjustments",
                columns: new[] { "UserId", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollApprovals_ApprovedById",
                table: "PayrollApprovals",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollApprovals_PaidById",
                table: "PayrollApprovals",
                column: "PaidById");

            migrationBuilder.CreateIndex(
                name: "UX_PayrollApprovals_UserId_PeriodStart",
                table: "PayrollApprovals",
                columns: new[] { "UserId", "PeriodStart" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollAdjustments");

            migrationBuilder.DropTable(
                name: "PayrollApprovals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TeacherRates_Multiplier",
                table: "TeacherRates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TeacherRates_Rates_NonNegative",
                table: "TeacherRates");

            migrationBuilder.DropColumn(
                name: "ActiveStudentBonusRate",
                table: "TeacherRates");

            migrationBuilder.DropColumn(
                name: "BaseSalary",
                table: "TeacherRates");

            migrationBuilder.DropColumn(
                name: "WeekendHolidayMultiplier",
                table: "TeacherRates");

            migrationBuilder.DropColumn(
                name: "PremiumMultiplierApplied",
                table: "SessionPayouts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TeacherRates_Rates_NonNegative",
                table: "TeacherRates",
                sql: "(\"PerSessionRate\" >= 0 AND \"PerStudentBonusRate\" >= 0)");
        }
    }
}
