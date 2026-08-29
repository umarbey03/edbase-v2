using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// KURSGA ARIZA (2026-08-28) — landing sahifadan kelgan so'rov
/// ════════════════════════════════════════════════════════════════════════
///
/// Loyiha egasining qarori: saytda "kursga yozilish" formasi bo'lsin,
/// lekin O'Z-O'ZIDAN RO'YXATDAN O'TISH BO'LMASIN.
///
/// ══════════════════════════════════════════════════════════════════════
/// 🔴 BU YOZUV HISOB EMAS VA HECH QANDAY KIRISH HUQUQI BERMAYDI
///
/// Ariza <see cref="User"/> jadvaliga UMUMAN tegmaydi. U alohida
/// jadvalda yashaydi va uning yagona ta'siri — o'quv bo'limi panelida
/// bitta qator paydo bo'lishi.
///
/// NIMA UCHUN SHUNDAY: agar forma profil yaratganida, istalgan odam
/// o'ziga hisob ochib olardi. Bu bot uchun ALLAQACHON yopilgan yo'l
/// (`TelegramUpdateHandler.HandleContactAsync` — "bot AKKAUNT
/// YARATMAYDI"), va uni saytdan qayta ochish o'sha qarorni bekor
/// qilardi. Hisobni faqat o'quv bo'limi ochadi.
/// ══════════════════════════════════════════════════════════════════════
///
/// ★ O'CHIRILMAYDI, HOLATI O'ZGARADI: "nechta ariza keldi, nechtasi
/// o'quvchiga aylandi" — markaz uchun asosiy o'lchov (konversiya).
/// O'chirilgan qator uni jimgina buzardi va "bu oy kam ariza keldi"
/// degan noto'g'ri xulosa berardi.
///
/// ★ TELEFON IKKI USTUNDA: ko'rsatish uchun XOM ko'rinish
/// (<see cref="Phone"/>) va taqqoslash uchun normal ko'rinish
/// (<see cref="PhoneNormalized"/>) — <see cref="User"/> dagi AYNI
/// naqsh va AYNI metod (<see cref="User.NormalizePhone"/>). Ikkinchi
/// normalizatsiya qoidasi yozilsa, "bu raqam allaqachon ariza
/// qoldirgan" tekshiruvi ikki xil ishlardi.
/// </summary>
public class EnrollmentApplication : BaseEntity
{
    public const int MaxFullNameLength = 120;
    public const int MaxPhoneLength = 32;
    public const int MaxCourseLength = 100;
    public const int MaxNoteLength = 500;
    public const int MaxCommentLength = 500;

    /// <summary>Ariza qoldirgan odamning ismi (o'zi yozgan ko'rinishda).</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Telefon — foydalanuvchi yozgan XOM ko'rinishda.</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Taqqoslash uchun normal ko'rinish (<see cref="User.NormalizePhone"/>).
    ///
    /// ★ INDEKS SHU USTUNDA: takroriy arizani topish uchun
    /// (<c>+998 90 123 45 67</c> va <c>998901234567</c> — bitta odam).
    /// </summary>
    public string PhoneNormalized { get; set; } = string.Empty;

    /// <summary>
    /// Qiziqtirgan yo'nalish — ERKIN MATN, katalogga havola EMAS.
    ///
    /// ★ NIMA UCHUN `CourseId` EMAS: landing'dagi ro'yxat marketing
    /// matni (`frontend/.../landing/model/content.ts`) va u bazadagi
    /// kurslar katalogi bilan bir xil emas. Yot kalitga bog'lansa,
    /// landing'da yangi yo'nalish paydo bo'lgan zahoti ariza SAQLANMAY
    /// qolardi — ya'ni marketing matnini o'zgartirish formani buzardi.
    ///
    /// <c>null</c> — odam tanlamagan ("hali bilmayman"), va bu normal
    /// holat: aynan shu savolga javob berish uchun qo'ng'iroq qilinadi.
    /// </summary>
    public string? Course { get; set; }

    /// <summary>Ariza qoldirgan odamning izohi. <c>null</c> — yozmagan.</summary>
    public string? Note { get; set; }

    /// <summary>Ish holati.</summary>
    public EnrollmentApplicationStatus Status { get; set; } = EnrollmentApplicationStatus.New;

    /// <summary>
    /// Operatorning izohi (qo'ng'iroq natijasi).
    /// Ariza qoldirgan odamning <see cref="Note"/> idan ALOHIDA: birini
    /// ikkinchisi ustiga yozish mijoz aytgan gapni yo'qotardi.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>Arizani oxirgi marta kim ishlagani. <c>null</c> — hali hech kim.</summary>
    public long? HandledByUserId { get; set; }

    public User? HandledBy { get; set; }

    /// <summary>Oxirgi holat o'zgarishi vaqti. <c>null</c> — hali o'zgarmagan.</summary>
    public DateTimeOffset? HandledAt { get; set; }

    /// <summary>
    /// Kelgan qiymatlarni tekshiradi va yozadi.
    ///
    /// ★ KESIB QO'YILADI, RAD ETILMAYDI (izoh va yo'nalish uchun): forma
    /// anonim va uzun matn — hujum emas, odatiy holat. Rad etish
    /// foydalanuvchiga "arizangiz ketmadi" deb aytardi, sabab esa unga
    /// tushunarsiz bo'lardi. Ism va telefon esa MAJBURIY: ularsiz ariza
    /// ma'nosiz.
    /// </summary>
    public void Apply(string? fullName, string? rawPhone, string? course, string? note)
    {
        var name = (fullName ?? string.Empty).Trim();

        if (name.Length == 0)
            throw new DomainException("Ism va familiyangizni kiriting.");

        var normalized = User.NormalizePhone(rawPhone);

        if (normalized is null)
            throw new DomainException("Telefon raqamini to'g'ri kiriting.");

        FullName = Truncate(name, MaxFullNameLength);
        PhoneNormalized = normalized;

        // Xom ko'rinish CHEGARADAN oshsa normal ko'rinishga tushamiz:
        // ustun 32 belgi va u yerga 200 belgilik "raqam" sig'maydi.
        var raw = (rawPhone ?? string.Empty).Trim();
        Phone = raw.Length is > 0 and <= MaxPhoneLength ? raw : normalized;

        Course = Clean(course, MaxCourseLength);
        Note = Clean(note, MaxNoteLength);
    }

    /// <summary>
    /// Holatni o'zgartiradi va kim qilganini yozadi.
    /// </summary>
    /// <remarks>
    /// ★ UCHALASI BIRGA YOZILADI (<see cref="Status"/>,
    /// <see cref="HandledByUserId"/>, <see cref="HandledAt"/>) — qo'lda
    /// yozilsa ulardan bittasi unutilib, "holat o'zgargan, lekin kim
    /// o'zgartirgani noma'lum" yozuvi paydo bo'lardi
    /// (<see cref="User.LinkTelegram"/> bilan AYNI mulohaza).
    /// </remarks>
    public void Handle(EnrollmentApplicationStatus status, string? comment, long actorId, DateTimeOffset now)
    {
        Status = status;
        Comment = Clean(comment, MaxCommentLength);
        HandledByUserId = actorId;
        HandledAt = now;
        UpdatedAt = now;
    }

    /// <summary>Bo'sh matnni <c>null</c> ga aylantiradi va chegarada kesadi.</summary>
    private static string? Clean(string? value, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : Truncate(trimmed, maxLength);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
