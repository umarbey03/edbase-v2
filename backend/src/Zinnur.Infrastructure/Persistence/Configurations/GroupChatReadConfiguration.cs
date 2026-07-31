using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class GroupChatReadConfiguration : IEntityTypeConfiguration<GroupChatRead>
{
    public void Configure(EntityTypeBuilder<GroupChatRead> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GroupChatReads");
        builder.HasKey(r => r.Id);

        // Guruh yoki foydalanuvchi o'chsa belgi ham keraksiz — Cascade.
        // Belgi hech qanday tarixiy qiymatga ega emas (u shunchaki
        // "qayergacha o'qidim" kursori), shuning uchun uni saqlab qolish
        // sababi yo'q.
        builder.HasOne(r => r.Group)
            .WithMany()
            .HasForeignKey(r => r.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================================
        // ★ UNIKAL INDEKS: (UserId, GroupId, Channel)
        //
        // Ikki vazifa bajaradi:
        //
        //  1) YAXLITLIK. Bitta foydalanuvchining bitta oqimda IKKITA
        //     chegarasi bo'lishi mumkin emas. Ikkita bo'lsa, qaysi biri
        //     to'g'ri ekani NOANIQ bo'lardi va o'qilmaganlar soni
        //     so'rovdan so'rovga sakrab turardi. Poyga (bir vaqtda ikki
        //     so'rov) aynan shu yerda to'xtaydi.
        //
        //  2) TEZLIK. "Chatlar" hubidagi sanoq
        //     (`WHERE UserId=@me AND GroupId=...`) shu indeksdan o'qiladi.
        //     Ustun tartibi ATAYLAB `UserId` dan boshlanadi: hamma so'rov
        //     "MENING belgilarim" deb boshlanadi, guruh esa keyin
        //     toraytiradi.
        // ============================================================
        builder.HasIndex(r => new { r.UserId, r.GroupId, r.Channel })
            .IsUnique()
            .HasDatabaseName("IX_GroupChatReads_User_Group_Channel");
    }
}
