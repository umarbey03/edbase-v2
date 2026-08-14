using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class GroupChatMessageConfiguration : IEntityTypeConfiguration<GroupChatMessage>
{
    public void Configure(EntityTypeBuilder<GroupChatMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("GroupChatMessages");
        builder.HasKey(m => m.Id);

        // Domain cheklovi bilan AYNAN bir xil (GroupChatMessage.MaxBodyLength).
        builder.Property(m => m.Body)
            .IsRequired()
            .HasMaxLength(GroupChatMessage.MaxBodyLength);

        builder.Property(m => m.SenderName)
            .IsRequired()
            .HasMaxLength(GroupChatMessage.MaxSenderNameLength);

        // ============================================================
        // GURUH O'CHIRILSA XABARLAR HAM O'CHADI (Cascade).
        //
        // Guruh chati guruhning O'ZIGA tegishli: guruhsiz xabar
        // "yetim" qator bo'lib qolardi va uni hech kim ko'ra olmasdi
        // (ruxsat guruh orqali hisoblanadi). Amalda guruhlar
        // ARXIVLANADI (`IsActive = false`), o'chirilmaydi — ya'ni bu
        // yo'l faqat haqiqiy tozalashda ishlaydi.
        // ============================================================
        builder.HasOne(m => m.Group)
            .WithMany()
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // `SenderId` uchun FK ATAYLAB YARATILMAYDI — `ChatMessage` dagi
        // bilan bir xil sabab: yuboruvchi ismi va roli xabar bilan birga
        // saqlangani uchun o'qishda `Users` ga JOIN kerak emas. Qo'shimcha
        // FK faqat har INSERT'da satr qulfi va yozuv narxini berardi.
        //
        // Yaxlitlik yo'qolmaydi: `SenderId` ni faqat Domain to'ldiradi va
        // u har doim autentifikatsiyadan o'tgan foydalanuvchining Id'si.

        // ============================================================
        // ★ ASOSIY INDEKS: (GroupId, Channel, Id)
        //
        // Tarix so'rovi AYNAN shunday:
        //   WHERE GroupId=@g AND Channel=@c AND Id < @cursor
        //   ORDER BY Id DESC LIMIT 51
        //
        // Kompozit indeks filtrni ham, tartibni ham, kursorni ham
        // qoplaydi — sahifalash uchun jadvalga umuman kirilmaydi.
        //
        // ★ KANAL USTUNI INDEKSNING IKKINCHI O'RNIDA (GroupId dan keyin,
        // Id dan oldin) — bu MAJBURIY. Kanalsiz indeksda Postgres ikkala
        // oqimni birga o'qib, keraksizini filtrda tashlardi: 1000 ta
        // kurator xabari orasidan 50 ta ustoz xabarini topish uchun butun
        // guruh tarixini varaqlashga to'g'ri kelardi.
        // ============================================================
        builder.HasIndex(m => new { m.GroupId, m.Channel, m.Id })
            .HasDatabaseName("IX_GroupChatMessages_Group_Channel_Id");

        // ============================================================
        // ★ O'QILMAGANLAR INDEKSI: (GroupId, Channel, SenderId, Id)
        //
        // "Chatlar" hubi har ochilganda har oqim uchun shu sanoqni
        // qiladi:
        //   WHERE GroupId=@g AND Channel=@c AND SenderId<>@me AND Id>@lastRead
        //
        // Asosiy indeks ham ishlardi, lekin `SenderId` filtri jadvalga
        // qaytishni talab qilardi (index scan + heap fetch). Bu yerda
        // sanoq FAQAT indeksdan bajariladi (index-only scan).
        // ============================================================
        builder.HasIndex(m => new { m.GroupId, m.Channel, m.SenderId, m.Id })
            .HasDatabaseName("IX_GroupChatMessages_Unread");

        // ============================================================
        // ★ TOZALASH INDEKSI: (SentAt, Id)
        //
        // Yagona o'quvchisi — `ChatRetentionJob`:
        //   WHERE SentAt < @cutoff ORDER BY SentAt, Id LIMIT @batch
        //
        // 🔴 NIMA UCHUN KERAK: yuqoridagi ikkala indeks ham `GroupId` dan
        // BOSHLANADI, ya'ni ularning bittasi ham vaqt bo'yicha filtrga
        // yaramaydi. Indekssiz bu so'rov eng katta jadvalda KETMA-KET
        // SKAN bo'lardi — va u SOATIGA bir marta, o'chiradigan narsa
        // BO'LMAGANDA ham yurardi. Indeks bilan esa "eskisi bormi?"
        // degan savolga javob bitta seek bilan olinadi.
        //
        // ★ `Id` IKKINCHI USTUN — saralash uchun: bir xil `SentAt` li
        // qatorlarda tartib aniq bo'ladi va paket indeksning O'ZIDAN
        // o'qiladi (jadvalga kirilmaydi).
        //
        // ⚠️ NARXI: har `INSERT` da bitta qo'shimcha B-tree yozuvi. Bu
        // yozish yo'li ODAM tezligida (o'quvchi savol yozadi), ya'ni
        // narx sezilmaydi. Muqobil yechim — `Id` monotonligiga tayanish
        // — YOZILMAGAN taxminga asoslanardi va buzilganda tozalash
        // JIMGINA to'xtardi; sabab batafsil `ChatRetentionJob` izohida.
        // ============================================================
        builder.HasIndex(m => new { m.SentAt, m.Id })
            .HasDatabaseName("IX_GroupChatMessages_SentAt");
    }
}
