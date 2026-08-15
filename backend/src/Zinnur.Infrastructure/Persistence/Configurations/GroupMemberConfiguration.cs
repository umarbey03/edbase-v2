using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Application.Groups;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GroupMembers");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Status).HasConversion<int>();

        // `IsActive` — hisoblanuvchi property (Status'dan), ustun EMAS.
        builder.Ignore(m => m.IsActive);

        // PAUZA MUDDATI — SOYA (shadow) ustun.
        //
        // `POST /groups/{id}/members/{studentId}/pause` ixtiyoriy `pausedUntil`
        // sanasini qabul qiladi, lekin `GroupMember` entity'sida bunday maydon
        // yo'q va Domain qatlami bu ish doirasida o'zgartirilmaydi. Soya ustun
        // bilan qiymat BAZAGA yoziladi va o'qiladi (`EF.Property<DateOnly?>`),
        // ya'ni klient yuborgan sana JIMGINA YO'QOLMAYDI.
        //
        // Batafsil va keyingi qadam: `Zinnur.Application.Groups.GroupMemberFields`.
        builder.Property<DateOnly?>(GroupMemberFields.PausedUntil).HasColumnType("date");

        builder.Property(m => m.Reason).HasMaxLength(GroupMember.MaxReasonLength);

        builder.HasOne(m => m.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Student)
            .WithMany()
            .HasForeignKey(m => m.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // TARIX: xodim yoki nishon guruh o'chsa ham yozuv QOLADI — bu
        // o'quvchining guruh tarixi, ular o'chganda yo'qolmasligi kerak
        // (`SessionReview.Author` bilan AYNI `Restrict` mulohazasi).
        builder.HasOne(m => m.LeftBy)
            .WithMany()
            .HasForeignKey(m => m.LeftById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.MovedToGroup)
            .WithMany()
            .HasForeignKey(m => m.MovedToGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // BITTA o'quvchi bitta guruhda BITTA marta. Eski tizimda bu faqat
        // kodda tekshirilardi va parallel so'rovlarda dublikat a'zolik
        // yaratilib, davomat ikki marta hisoblanardi.
        builder.HasIndex(m => new { m.GroupId, m.StudentId })
            .IsUnique()
            .HasDatabaseName("UX_GroupMembers_GroupId_StudentId");

        // "Men qaysi guruhlardaman" so'rovi uchun (LiveSessionService.ListForUserAsync).
        builder.HasIndex(m => new { m.StudentId, m.Status })
            .HasDatabaseName("IX_GroupMembers_StudentId_Status");

        // "SHU GURUHDA kim FAOL o'qiyapti" — foydalanuvchilar ro'yxatining
        // `groupId` filtri (`GET /users?groupId=...`).
        //
        // NIMA UCHUN MAVJUD INDEKSLAR YETMAYDI:
        //  • `UX_GroupMembers_GroupId_StudentId` — `group_id` prefiksiga
        //    xizmat qiladi, lekin `status` unda YO'Q: Postgres indeksdan
        //    guruhning BARCHA a'zoligini o'qib, keyin `status` ni qator
        //    darajasida filtrlardi (chiqarilgan o'quvchilari ko'p bo'lgan
        //    guruhda bu ortiqcha ish);
        //  • `IX_GroupMembers_StudentId_Status` — prefiksi `student_id`,
        //    ya'ni guruh bo'yicha qidiruvda umuman ishlamaydi.
        builder.HasIndex(m => new { m.GroupId, m.Status })
            .HasDatabaseName("IX_GroupMembers_GroupId_Status");
    }
}
