using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttritionReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReasonId",
                table: "GroupMembershipEvents",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttritionReasons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttritionReasons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembershipEvents_ReasonId",
                table: "GroupMembershipEvents",
                column: "ReasonId");

            migrationBuilder.CreateIndex(
                name: "UX_AttritionReasons_Label",
                table: "AttritionReasons",
                column: "Label",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMembershipEvents_AttritionReasons_ReasonId",
                table: "GroupMembershipEvents",
                column: "ReasonId",
                principalTable: "AttritionReasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ═══════════════════════════════════════════════════════════
            // BOSHLANG'ICH RO'YXAT — markazlarda eng ko'p uchraydigan
            // sabablar. O'quv bo'limi ularni "Sozlamalar → To'kilish
            // sabablari" bo'limida tahrirlaydi, o'chiradi va qo'shadi.
            //
            // ★ NIMA UCHUN BO'SH JADVAL BILAN BOSHLANMAYDI: ro'yxat bo'sh
            //   bo'lsa, chiqarish oynasidagi tanlov ham bo'sh chiqardi va
            //   operator birinchi kundan sababsiz ishlashga majbur
            //   bo'lardi — ya'ni aynan hal qilmoqchi bo'lgan muammomiz
            //   (foizsiz hisobot) qaytadan paydo bo'lardi.
            // ═══════════════════════════════════════════════════════════
            migrationBuilder.Sql("""
                INSERT INTO "AttritionReasons" ("Label", "IsActive", "CreatedAt")
                VALUES
                    ('Moliyaviy qiyinchilik', TRUE, NOW()),
                    ('Vaqt mos kelmadi', TRUE, NOW()),
                    ('Boshqa shaharga ko''chib ketdi', TRUE, NOW()),
                    ('Sog''liq sabab', TRUE, NOW()),
                    ('Ta''til / vaqtinchalik tanaffus', TRUE, NOW()),
                    ('O''qish sifatidan norozi', TRUE, NOW()),
                    ('Ustoz bilan kelisha olmadi', TRUE, NOW()),
                    ('Maktab/ish bilan to''qnashdi', TRUE, NOW()),
                    ('Boshqa o''quv markaziga ketdi', TRUE, NOW()),
                    ('Aloqaga chiqmadi', TRUE, NOW()),
                    ('Boshqa sabab', TRUE, NOW())
                ON CONFLICT ("Label") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupMembershipEvents_AttritionReasons_ReasonId",
                table: "GroupMembershipEvents");

            migrationBuilder.DropTable(
                name: "AttritionReasons");

            migrationBuilder.DropIndex(
                name: "IX_GroupMembershipEvents_ReasonId",
                table: "GroupMembershipEvents");

            migrationBuilder.DropColumn(
                name: "ReasonId",
                table: "GroupMembershipEvents");
        }
    }
}
