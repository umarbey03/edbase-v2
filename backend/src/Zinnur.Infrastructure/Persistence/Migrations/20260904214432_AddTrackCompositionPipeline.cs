using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackCompositionPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ════════════════════════════════════════════════════════════
            // 1) MA'LUMOTNI TUZATISH — YANGI UNIKAL INDEKSDAN OLDIN.
            //
            // `UX_SessionRecordings_SessionId_Pipeline_Active` bir darsda
            // bir yo'ldan FAQAT BITTA yakunlanmagan urinish bo'lishini
            // talab qiladi. Bazada eski, osilib qolgan ikkinchi urinish
            // bo'lsa indeks QURILMAYDI va migratsiya butunlay to'xtaydi.
            //
            // Quyidagi so'rov har dars uchun ENG YANGI yakunlanmagan
            // qatorni qoldiradi, undan eskilarini `Failed` (4) qiladi —
            // ular allaqachon o'lik urinishlar, ularning fayli hech qachon
            // kelmaydi.
            //
            // ⚠️ KUTILAYOTGAN NATIJA: 0 qator. `AutoRecordingScheduler`
            //    buni allaqachon to'sadi. Noldan farqli son — xabar
            //    berishga arziydi, lekin to'xtashga emas.
            //
            // 🔴 `Down` BU QADAMNI QAYTARMAYDI: qaysi qator qachon va
            //    qaysi holatdan o'zgargani saqlanmaydi, va o'zgargan
            //    qatorlar YANGI holatida TO'G'RI — ular haqiqatan
            //    yiqilgan urinishlar.
            // ════════════════════════════════════════════════════════════
            migrationBuilder.Sql("""
                UPDATE "SessionRecordings" r
                SET "Status" = 4,
                    "Error"  = 'Eski, yakunlanmagan yozuv urinishi — yangi indeks talabi bilan yopildi.',
                    "UpdatedAt" = now()
                WHERE r."Status" < 3
                  AND EXISTS (
                    SELECT 1 FROM "SessionRecordings" o
                    WHERE o."SessionId" = r."SessionId"
                      AND o."Status" < 3
                      AND o."Id" > r."Id");
                """);

            // ════════════════════════════════════════════════════════════
            // 2) `SessionRecordings` — 9 ta QO'SHIMCHA ustun.
            //
            // ⚠️ `defaultValue: 0` MAVJUD QATORLARNI to'ldirish uchun
            //    (ustun `NOT NULL`). `0` = `RecordingPipeline.RoomComposite`
            //    = bugungi xatti-harakat, ya'ni ishlab chiqarishdagi har
            //    bir qator hech qanday ma'lumot ko'chirishsiz TO'G'RI
            //    qoladi. Modelda ustun DEFAULT'i yo'q — EF qiymatni har
            //    doim oshkor yozadi (izoh `SessionRecordingConfiguration` da).
            // ════════════════════════════════════════════════════════════
            migrationBuilder.AddColumn<int>(
                name: "CompositionAttempts",
                table: "SessionRecordings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CompositionError",
                table: "SessionRecordings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompositionFinishedAt",
                table: "SessionRecordings",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompositionInterruptions",
                table: "SessionRecordings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompositionLeaseUntil",
                table: "SessionRecordings",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompositionStartedAt",
                table: "SessionRecordings",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompositionStatus",
                table: "SessionRecordings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Pipeline",
                table: "SessionRecordings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RawPurgedAt",
                table: "SessionRecordings",
                type: "timestamptz",
                nullable: true);

            // ════════════════════════════════════════════════════════════
            // 3) `Groups` — 1 ta qo'shimcha ustun. Mavjud 33 guruh `0`
            //    (= `RoomComposite`) oladi, ya'ni ularning darslari
            //    avvalgidek yoziladi.
            // ════════════════════════════════════════════════════════════
            migrationBuilder.AddColumn<int>(
                name: "RecordingPipeline",
                table: "Groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // ════════════════════════════════════════════════════════════
            // 4) `RecordingTracks` — YANGI jadval (izoh `RecordingTrack` da).
            //    5) undan keyingi `UX_SessionRecordings_SessionId_Pipeline_Active`
            //    — yuqoridagi tuzatish AYNAN shu indeks uchun qilingan.
            // ════════════════════════════════════════════════════════════
            migrationBuilder.CreateTable(
                name: "RecordingTracks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecordingId = table.Column<long>(type: "bigint", nullable: false),
                    TrackSid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParticipantIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EgressId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    ProbedDurationMs = table.Column<int>(type: "integer", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    StopRequestedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordingTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecordingTracks_SessionRecordings_RecordingId",
                        column: x => x.RecordingId,
                        principalTable: "SessionRecordings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_SessionRecordings_SessionId_Pipeline_Active",
                table: "SessionRecordings",
                columns: new[] { "SessionId", "Pipeline" },
                unique: true,
                filter: "\"Status\" < 3");

            migrationBuilder.CreateIndex(
                name: "IX_RecordingTracks_RecordingId_Kind_StartedAt",
                table: "RecordingTracks",
                columns: new[] { "RecordingId", "Kind", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordingTracks_Status_LastAttemptAt",
                table: "RecordingTracks",
                columns: new[] { "Status", "LastAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "UX_RecordingTracks_EgressId",
                table: "RecordingTracks",
                column: "EgressId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RecordingTracks_RecordingId_TrackSid",
                table: "RecordingTracks",
                columns: new[] { "RecordingId", "TrackSid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 🔴 `Up` NING 1-QADAMI (ma'lumot tuzatish) QAYTARILMAYDI —
            //    sabab o'sha yerda yozilgan. Qolgan hammasi qo'shimcha
            //    edi, shuning uchun teskari yo'l to'liq va xavfsiz:
            //    hech qanday MAVJUD ustun yoki indeksga tegilmagan.
            migrationBuilder.DropTable(
                name: "RecordingTracks");

            migrationBuilder.DropIndex(
                name: "UX_SessionRecordings_SessionId_Pipeline_Active",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "CompositionAttempts",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "CompositionError",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "CompositionFinishedAt",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "CompositionInterruptions",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "CompositionLeaseUntil",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "CompositionStartedAt",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "CompositionStatus",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "Pipeline",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "RawPurgedAt",
                table: "SessionRecordings");

            migrationBuilder.DropColumn(
                name: "RecordingPipeline",
                table: "Groups");
        }
    }
}
