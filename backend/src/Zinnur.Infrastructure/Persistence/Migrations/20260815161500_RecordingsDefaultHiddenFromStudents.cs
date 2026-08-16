using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zinnur.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordingsDefaultHiddenFromStudents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ★ FAQAT USTUN DEFAULT'i almashadi — mavjud qatorlarning
            // saqlangan qiymati TEGILMAYDI (backfill YO'Q, ataylab: sabab
            // `SessionRecording.IsVisibleToStudents` izohida). Bugungача
            // yakunlangan yozuvlar avvalgidek ko'rinishda qoladi; faqat
            // ENDI qo'shiladigan qatorlar (qiymat ko'rsatilmasa) yashirin
            // holatda boshlanadi.
            migrationBuilder.AlterColumn<bool>(
                name: "IsVisibleToStudents",
                table: "SessionRecordings",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsVisibleToStudents",
                table: "SessionRecordings",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);
        }
    }
}
