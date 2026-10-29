using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineShopOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "SubscriptionPlans",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "SubscriptionPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "SubscriptionPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    StudentId = table.Column<long>(type: "bigint", nullable: false),
                    PlanId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CourseId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecidedById = table.Column<long>(type: "bigint", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SubscriptionPeriodId = table.Column<long>(type: "bigint", nullable: true),
                    PaymentTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.CheckConstraint("CK_PurchaseOrders_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_PaymentTransactions_PaymentTransactionId",
                        column: x => x.PaymentTransactionId,
                        principalTable: "PaymentTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_SubscriptionPeriods_SubscriptionPeriodId",
                        column: x => x.SubscriptionPeriodId,
                        principalTable: "SubscriptionPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_SubscriptionPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Users_DecidedById",
                        column: x => x.DecidedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_Code",
                table: "PurchaseOrders",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CourseId",
                table: "PurchaseOrders",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_DecidedById",
                table: "PurchaseOrders",
                column: "DecidedById");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_PaymentTransactionId",
                table: "PurchaseOrders",
                column: "PaymentTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_PlanId",
                table: "PurchaseOrders",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_Status_CreatedAt",
                table: "PurchaseOrders",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_StudentId_CreatedAt",
                table: "PurchaseOrders",
                columns: new[] { "StudentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SubscriptionPeriodId",
                table: "PurchaseOrders",
                column: "SubscriptionPeriodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "SubscriptionPlans");
        }
    }
}
