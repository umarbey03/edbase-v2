using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ilova ichidagi bildirishnoma jadvali.
///
/// ★ <c>MessageOutbox</c> BILAN ARALASHTIRILMAYDI: u yerda YETKAZIB
/// BERISH holati (urinishlar, keyingi urinish vaqti, kanal), bu yerda esa
/// KO'RSATISH holati (o'qildimi). Sabab <see cref="Notification"/> izohida.
/// </summary>
public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);

        // Foydalanuvchi o'chsa bildirishnomalari ham keraksiz — Cascade.
        // Ular hech qanday tarixiy/moliyaviy qiymatga ega emas (audit
        // izlaridan farqi shu), shuning uchun saqlab qolish sababi yo'q.
        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Enum `int` bo'lib yoziladi (EF standarti) — `NotificationKind`
        // izohidagi "raqamlar hech qachon surilmaydi" qoidasi shu sababdan.
        builder.Property(n => n.Kind)
            .IsRequired();

        builder.Property(n => n.Title)
            .IsRequired()
            .HasMaxLength(Notification.MaxTitleLength);

        builder.Property(n => n.Body)
            .IsRequired()
            .HasMaxLength(Notification.MaxBodyLength);

        // ================================================================
        // ★ YAGONA INDEKS: (UserId, ReadAt, CreatedAt)
        //
        // Uch so'rovga xizmat qiladi va ustun tartibi AYNAN shu uchtaning
        // shakliga qarab tanlangan:
        //
        //  1) O'QILMAGANLAR SANOG'I — qo'ng'iroqchadagi raqam:
        //     `WHERE UserId=@me AND ReadAt IS NULL`
        //     Bu HAR sahifa ochilishida so'raladigan eng tez-tez so'rov,
        //     shuning uchun u indeksdan TO'LIQ o'qiladi (jadvalga
        //     umuman tushmaydi).
        //
        //  2) "FAQAT O'QILMAGANLAR" ro'yxati — o'sha prefiks + sana
        //     bo'yicha tartib, ya'ni saralashsiz.
        //
        //  3) TO'LIQ RO'YXAT — `WHERE UserId=@me ORDER BY Id DESC`.
        //     ⚠️ Bu so'rov indeksning faqat BIRINCHI ustunini ishlatadi va
        //     saralashni o'zi bajaradi. BU ONGLI TANLOV, kamchilik emas:
        //     bitta foydalanuvchining qatorlari yuzlab, ya'ni saralash
        //     narxi sezilmaydi; ikkinchi indeks esa HAR yozuvda (baholash
        //     tranzaksiyasi ichida!) qo'shimcha yozish demakdir. Qatorlar
        //     soni o'sib ketsa yechim indeks emas, TOZALASH bo'ladi.
        //
        // ★ NIMA UCHUN KURSOR `CreatedAt` EMAS, `Id`: `bigserial` qat'iy
        //   o'suvchi va YAGONA. Vaqt bo'yicha kursor bir xil millisekundda
        //   yozilgan ikki qatorni ajrata olmasdi (ustoz 50 ta ishni ketma-ket
        //   baholaganda bu real holat) va soat surilganda (NTP) orqaga
        //   ketardi — `GroupChatRead.LastReadMessageId` dagi AYNI sabab.
        // ================================================================
        builder.HasIndex(n => new { n.UserId, n.ReadAt, n.CreatedAt })
            .HasDatabaseName("IX_Notifications_User_Read_Created");
    }
}
