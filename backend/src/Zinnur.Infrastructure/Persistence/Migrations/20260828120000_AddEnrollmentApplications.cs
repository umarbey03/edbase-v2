using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Kursga arizalar jadvali (2026-08-28) — landing sahifadagi forma.
    ///
    /// 🔴 BU MIGRATSIYA QO'LDA YOZILGAN (`dotnet ef migrations add` EMAS):
    ///    kod yozilgan mashinada .NET SDK yo'q edi. Tuzilma
    ///    `EnrollmentApplicationConfiguration` bilan qo'lda solishtirildi
    ///    va modelning kesimi (`ApplicationDbContextModelSnapshot`) ham
    ///    qo'lda yangilandi.
    ///
    ///    ⚠️ KEYINGI MIGRATSIYANI YARATISHDAN OLDIN buni tekshiring:
    ///       `dotnet ef migrations add Tekshiruv` bo'sh `Up`/`Down`
    ///       bergani — kesim to'g'ri ekanini bildiradi (keyin uni
    ///       o'chirib tashlang). Bo'sh bo'lmasa, farqni shu yerga
    ///       ko'chiring.
    ///
    /// ★ SEED YO'Q: ariza jadvali bo'sh boshlanadi (`AttritionReasons`
    ///   dan farqli — u ro'yxat/katalog edi, bu esa hodisa jurnali).
    /// </summary>
    public partial class AddEnrollmentApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnrollmentApplications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PhoneNormalized = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Course = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HandledByUserId = table.Column<long>(type: "bigint", nullable: true),
                    HandledAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentApplications", x => x.Id);

                    // `Restrict` — arizani ishlagan xodim o'chirilsa, ariza
                    // O'CHMASIN (sabab konfiguratsiya faylida: konversiya
                    // tarixi xodimlar bilan birga yo'qolardi).
                    table.ForeignKey(
                        name: "FK_EnrollmentApplications_Users_HandledByUserId",
                        column: x => x.HandledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentApplications_HandledByUserId",
                table: "EnrollmentApplications",
                column: "HandledByUserId");

            // UNIKAL EMAS — bitta odam qayta ariza qoldirishi mumkin
            // (sabab konfiguratsiya faylidagi ★ blokda).
            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentApplications_PhoneNormalized",
                table: "EnrollmentApplications",
                column: "PhoneNormalized");

            // Ro'yxatning asosiy so'rovi: holat bo'yicha filtr + eng
            // yangisi yuqorida.
            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentApplications_Status_CreatedAt",
                table: "EnrollmentApplications",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnrollmentApplications");
        }
    }
}
