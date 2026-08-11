using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class StudentNoteConfiguration : IEntityTypeConfiguration<StudentNote>
{
    public void Configure(EntityTypeBuilder<StudentNote> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StudentNotes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Body).IsRequired().HasMaxLength(StudentNote.MaxBodyLength);

        // O'QUVCHI: `Restrict` — izohi bor profilni o'chirib bo'lmaydi.
        // Amalda profil o'chirilmaydi ham (`deactivate` — yumshoq o'chirish),
        // shuning uchun bu chegara faqat qo'lda `DELETE` ni to'sadi.
        builder.HasOne(n => n.Student)
            .WithMany()
            .HasForeignKey(n => n.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // MUALLIF: `Restrict` — loyihadagi izchil qoida (`AttendanceAudit.Actor`
        // bilan bir xil): xodim o'chirilsa ham "kim yozgan" yo'qolmasin.
        // Izoh anonim bo'lib qolsa uning ishonchliligi tushardi — "kech
        // qoladi" degan yozuvni kim qo'yganini so'rash mumkin bo'lmasdi.
        builder.HasOne(n => n.Author)
            .WithMany()
            .HasForeignKey(n => n.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        // GURUH: `SetNull` — guruh o'chirilsa izoh QOLADI, faqat konteksti
        // yo'qoladi. Izoh o'quvchi HAQIDA, guruh haqida emas; `Restrict`
        // bo'lsa bitta izoh butun guruhni o'chirishni to'sib qo'yardi,
        // `Cascade` bo'lsa esa arxivlangan guruh bilan birga o'quvchi
        // haqidagi tarix ham izsiz ketardi.
        builder.HasOne(n => n.Group)
            .WithMany()
            .HasForeignKey(n => n.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        // "SHU o'quvchining izohlari, yangisidan eskisiga" — profil drawer'i
        // ochilganda bajariladigan YAGONA so'rov. `Id` kamayish tartibida:
        // ro'yxat aynan shunday o'qiladi, shu tufayli Postgres saralashni
        // umuman bajarmaydi (indeksdan teskari yurish ham mumkin, lekin
        // tartibni indeksda ochiq yozish niyatni kodda ko'rsatib turadi).
        builder.HasIndex(n => new { n.StudentId, n.Id })
            .IsDescending(false, true)
            .HasDatabaseName("IX_StudentNotes_StudentId_Id");
    }
}
