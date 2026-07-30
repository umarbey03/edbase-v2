using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class TestAnswerConfiguration : IEntityTypeConfiguration<TestAnswer>
{
    public void Configure(EntityTypeBuilder<TestAnswer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TestAnswers");
        builder.HasKey(a => a.Id);

        builder.HasOne(a => a.Attempt)
            .WithMany(t => t.Answers)
            .HasForeignKey(a => a.AttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Question)
            .WithMany()
            .HasForeignKey(a => a.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Option)
            .WithMany()
            .HasForeignKey(a => a.OptionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ====================================================================
        // ★★ LOYIHADAGI ENG MUHIM UNIKAL INDEKS — DIQQAT BILAN O'QING ★★
        //
        // Unikal juftlik AYNAN UCHTA ustundan: (Attempt, Question, Option).
        //
        // ESKI TIZIMDA bu (attempt_id, question_id) EDI — ya'ni bir savolga
        // BITTA javob qatori. Natijada KO'P TO'G'RI JAVOBLI savol umuman
        // ishlamasdi: o'quvchi uchta variantni belgilasa, ikkinchi qator
        // unikal indeksga urilib yiqilardi (yoki `ON CONFLICT` bilan ustiga
        // yozilib, faqat OXIRGI tanlov saqlanardi). Baholash esa "hammasi
        // yoki hech nima" qoidasida ishlagani uchun o'quvchi HAMMA to'g'ri
        // javobni belgilab ham 0 ball olardi.
        //
        // Uch ustunli indeks aynan kerakli kafolatni beradi:
        //   • bir savolga BIR NECHTA tanlov — MUMKIN (ko'p to'g'ri javob);
        //   • AYNI variant ikki marta — MUMKIN EMAS (takror yuborish,
        //     ikki marta bosish, parallel so'rov).
        //
        // Shu tufayli bir vaqtda ikki `submit` kelganda yutqazgani 23505
        // (unique violation) oladi va servis uni 409 ga aylantiradi —
        // ball ikki barobar hisoblanib ketmaydi.
        // ====================================================================
        builder.HasIndex(a => new { a.AttemptId, a.QuestionId, a.OptionId })
            .IsUnique()
            .HasDatabaseName("UX_TestAnswers_AttemptId_QuestionId_OptionId");

        // Varaqani ko'rish (`attempt detail`): urinish + savol bo'yicha o'qish.
        // Yuqoridagi unikal indeks prefiks sifatida ham xizmat qiladi, lekin
        // u UNIKAL — Postgres uni o'qish uchun ham ishlatadi, shuning uchun
        // qo'shimcha indeks KERAK EMAS (ortiqcha indeks yozishni sekinlashtiradi).
        builder.HasIndex(a => a.QuestionId)
            .HasDatabaseName("IX_TestAnswers_QuestionId");

        builder.HasIndex(a => a.OptionId)
            .HasDatabaseName("IX_TestAnswers_OptionId");
    }
}
