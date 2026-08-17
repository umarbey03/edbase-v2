using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// KURATOR ↔ O'QUVCHI SHAXSIY YOZISHMASI (DM)
/// ========================================================================
///
/// ★ BU <see cref="ChatMessage"/> EMAS. Ikkalasi ham "chat", lekin
/// mutlaqo boshqa hodisa va ATAYLAB boshqa jadvalda:
///
///   <see cref="ChatMessage"/>  — JONLI DARS xonasidagi ommaviy oqim.
///                                Dars tugagach o'lik tarixga aylanadi,
///                                200 kishi bir vaqtda yozadi, shuning
///                                uchun fon navbati orqali paketlab
///                                yoziladi (<c>ChatMessageWriter</c>).
///
///   <see cref="DirectMessage"/> — IKKI kishilik doimiy suhbat. Darsga
///                                bog'lanmagan, oylab davom etadi,
///                                o'qilgan/o'qilmagan holati bor va
///                                XABAR YO'QOLMASLIGI SHART (o'quvchining
///                                savoli). Shuning uchun navbat YO'Q:
///                                yozuv darhol va sinxron saqlanadi.
///
/// Ikkisini bitta jadvalga qo'shish "sessiya id'si bo'sh bo'lgan xabar"
/// degan yarim holat yaratardi va har so'rovda `WHERE SessionId IS NULL`
/// filtri kerak bo'lardi — bir joyda unutilsa o'quvchining shaxsiy
/// savoli butun dars chatida ko'rinib qolardi.
///
/// SUHBAT KALITI: <c>(StudentId, StaffId)</c>. Alohida "conversations"
/// jadvali ATAYLAB YO'Q — suhbat kim bilan kimligini guruh biriktiruvi
/// belgilaydi (<c>Group.AssistantId</c> / <c>Group.CuratorGroupId</c>),
/// ya'ni suhbat jadvali ikkinchi haqiqat manbai bo'lib, biriktiruv
/// o'zgarganda eskirib qolardi.
/// </summary>
public class DirectMessage : BaseEntity
{
    /// <summary>
    /// Maksimal uzunlik. Jonli dars chatidan (500) UZUNROQ: bu yerda
    /// o'quvchi vazifa bo'yicha batafsil savol yozadi, bir qatorli
    /// replika emas.
    /// </summary>
    public const int MaxBodyLength = 2000;

    /// <summary>Suhbatdagi O'QUVCHI (rolidan qat'i nazar, doim shu tomon).</summary>
    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>Suhbatdagi XODIM (kurator/yordamchi).</summary>
    public long StaffId { get; set; }

    public User? Staff { get; set; }

    /// <summary>
    /// Xabarni kim yozdi — <see cref="StudentId"/> yoki <see cref="StaffId"/>.
    /// Uchinchi qiymat bo'lishi mumkin emas (<see cref="Create"/> tekshiradi).
    /// </summary>
    public long SenderId { get; set; }

    /// <summary>
    /// Savol QAYSI kurs darsi sahifasidan yozilgan. <c>null</c> — umumiy savol.
    /// Kurator javob yozayotganda kontekstni ko'rishi uchun.
    /// </summary>
    public long? ModuleLessonId { get; set; }

    public ModuleLesson? ModuleLesson { get; set; }

    public required string Body { get; set; }

    /// <summary>O'quvchi ko'rdimi (o'qilmaganlar sanog'i uchun).</summary>
    public bool ReadByStudent { get; set; }

    /// <summary>Kurator ko'rdimi.</summary>
    public bool ReadByStaff { get; set; }

    public DateTimeOffset SentAt { get; set; }

    /// <summary>
    /// Biriktirilgan fayllar (rasm / ovoz / hujjat) — 2026-08-17, `GroupChatMessage`
    /// dagi R16b naqshining AYNI o'zi (sabab <see cref="DirectMessageAttachment"/> da).
    /// </summary>
    public ICollection<DirectMessageAttachment> Attachments { get; set; } =
        new List<DirectMessageAttachment>();

    // ---------------------------------------------------------------- hisoblanuvchi

    /// <summary>Xabarni o'quvchi yozganmi (aks holda kurator).</summary>
    public bool SentByStudent => SenderId == StudentId;

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Yangi MATNLI xabar: matn tozalanadi, bo'sh rad etiladi.
    ///
    /// ★ O'QILGAN BAYROQLARI SHU YERDA QO'YILADI, chaqiruvchida emas.
    /// Yuboruvchi o'z xabarini albatta "o'qigan" — aks holda o'quvchi
    /// yozgan savol o'ziga "o'qilmagan" bo'lib qaytardi va o'qilmaganlar
    /// sanog'i hech qachon nolga tushmasdi. Eski tizimda bu har
    /// endpointda qo'lda yozilardi (<c>read_by_student=True</c>) va
    /// kurator tomonida bir joyda tushib qolgandi.
    /// </summary>
    /// <param name="now">Joriy vaqt — ARGUMENT (Domain soatni bilmaydi).</param>
    public static DirectMessage Create(
        long studentId,
        long staffId,
        long senderId,
        long? moduleLessonId,
        string? body,
        DateTimeOffset now) =>
        Build(
            studentId, staffId, senderId, moduleLessonId,
            MessageText.Normalize(body, MaxBodyLength), now);

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════
    /// BIRIKTIRMALI XABAR (2026-08-17) — MATN IXTIYORIY
    /// ════════════════════════════════════════════════════════════════════
    ///
    /// `GroupChatMessage.CreateWithAttachments` bilan AYNI naqsh va AYNI
    /// sabab (`MessageText.NormalizeOptional` izohi): bo'sh matnga FAQAT
    /// shu yo'lda ruxsat beriladi va SHARTSIZ emas —
    /// <paramref name="attachmentCount"/> kamida 1 bo'lishi kerak.
    ///
    /// ⚠️ BIRIKTIRMALARNING O'ZI BU YERDA QO'SHILMAYDI — faqat SONI
    /// tekshiriladi (sabab o'sha izohda: ombor kaliti fayl R2'ga
    /// yozilgandan KEYIN ma'lum bo'ladi, Domain esa omborni bilmaydi).
    /// </summary>
    public static DirectMessage CreateWithAttachments(
        long studentId,
        long staffId,
        long senderId,
        long? moduleLessonId,
        string? body,
        int attachmentCount,
        DateTimeOffset now)
    {
        if (attachmentCount <= 0)
            throw new DomainException("Biriktirmasiz xabarda matn bo'lishi shart.");

        if (attachmentCount > DirectMessageAttachment.MaxPerMessage)
        {
            throw new DomainException(
                $"Bitta xabarga ko'pi bilan {DirectMessageAttachment.MaxPerMessage} ta fayl "
                + "biriktiriladi.");
        }

        return Build(
            studentId, staffId, senderId, moduleLessonId,
            MessageText.NormalizeOptional(body, MaxBodyLength), now);
    }

    /// <summary>
    /// Ikkala fabrikaning UMUMIY o'zagi — sabab `GroupChatMessage.Build`
    /// izohi bilan AYNI: bitta joyda bo'lmasa, tekshiruv ulardan birida
    /// unutilib qolardi.
    /// </summary>
    private static DirectMessage Build(
        long studentId,
        long staffId,
        long senderId,
        long? moduleLessonId,
        string body,
        DateTimeOffset now)
    {
        if (studentId == staffId)
            throw new DomainException("Suhbat ikki xil foydalanuvchi orasida bo'ladi.");

        if (senderId != studentId && senderId != staffId)
            throw new DomainException("Xabarni faqat suhbat ishtirokchisi yubora oladi.");

        var sentByStudent = senderId == studentId;

        return new DirectMessage
        {
            StudentId = studentId,
            StaffId = staffId,
            SenderId = senderId,
            ModuleLessonId = moduleLessonId,
            Body = body,
            ReadByStudent = sentByStudent,
            ReadByStaff = !sentByStudent,
            SentAt = now,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Xabarni <paramref name="readerId"/> uchun o'qilgan deb belgilaydi.
    /// Faqat QARSHI tomon yozgan xabar belgilanadi — o'z xabaringizni
    /// "o'qish" ma'nosiz va sanoqni buzardi.
    /// </summary>
    /// <returns>Holat haqiqatan o'zgardimi (idempotentlik uchun).</returns>
    public bool MarkRead(long readerId, DateTimeOffset now)
    {
        if (readerId != StudentId && readerId != StaffId)
            throw new DomainException("Xabarni faqat suhbat ishtirokchisi o'qiy oladi.");

        if (readerId == SenderId) return false;      // o'z xabari

        if (readerId == StudentId)
        {
            if (ReadByStudent) return false;
            ReadByStudent = true;
        }
        else
        {
            if (ReadByStaff) return false;
            ReadByStaff = true;
        }

        UpdatedAt = now;
        return true;
    }
}
