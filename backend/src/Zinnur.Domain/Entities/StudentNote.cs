using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// XODIMNING O'QUVCHI HAQIDAGI ICHKI IZOHI
/// ========================================================================
///
/// Eski tizimdagi <c>student_notes</c> jadvalining o'rnini bosadi (u v2 ga
/// ko'chirilmagan edi — <c>docs/MA_LUMOT_KOCHIRISH.md</c>).
///
/// 🔴 BU ICHKI YOZUV: "darsga kech qoladi", "otasi bilan gaplashildi",
/// "sinovdan o'tmadi" kabi matnlar bo'ladi. O'QUVCHI O'Z IZOHLARINI
/// KO'RMAYDI — ruxsat qoidasi <c>StudentNoteService</c> da va u yerda
/// <c>Student</c> roli uchun 403 qaytariladi. Shu sababli izoh matni HECH
/// QAYERDA o'quvchi ko'radigan javobga qo'shilmasligi kerak.
///
/// NIMA UCHUN MAVJUD <c>Submission.Feedback</c> MAYDONIGA QO'SHILMADI:
/// vazifa izohi o'quvchi UCHUN yoziladi va u ko'radi. Bu esa aksincha —
/// o'quvchidan YOPIQ. Ikkisini bir joyda saqlash ertami-kechmi ichki
/// eslatmani o'quvchiga ko'rsatib qo'yardi.
/// </summary>
/// <remarks>
/// <see cref="GroupId"/> — IXTIYORIY kontekst: "qaysi guruhdagi xatti-harakati
/// haqida". Guruh o'chirilsa izoh QOLADI (FK <c>SET NULL</c>) — izoh o'quvchi
/// haqida, guruh haqida emas.
/// </remarks>
public class StudentNote : BaseEntity
{
    public const int MaxBodyLength = 2000;

    /// <summary>Izoh KIM HAQIDA.</summary>
    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>Izohni KIM yozgan (ustoz, kurator yoki o'quv bo'limi).</summary>
    public long AuthorId { get; set; }

    /// <summary>
    /// Muallif — navigatsiya SHART: ro'yxatda har izohning ostida uning ismi
    /// ko'rinadi, ya'ni ism HAR o'qishda kerak bo'ladi.
    /// </summary>
    public User? Author { get; set; }

    /// <summary>Ixtiyoriy kontekst: qaysi guruh bo'yicha yozilgan.</summary>
    public long? GroupId { get; set; }

    public Group? Group { get; set; }

    public required string Body { get; set; }

    public static StudentNote Create(
        long studentId, long authorId, long? groupId, string? body, DateTimeOffset now) =>
        new()
        {
            StudentId = studentId,
            AuthorId = authorId,
            GroupId = groupId,
            Body = RequireBody(body),
            CreatedAt = now,
        };

    /// <summary>
    /// Matnni almashtiradi. Muallif va guruh konteksti O'ZGARMAYDI: tahrirlash
    /// izohni "boshqa odam yozgan" qilib ko'rsatish yo'li bo'lmasligi kerak
    /// (ruxsat qoidasi ham aynan muallifga tayanadi).
    /// </summary>
    public void Edit(string? body, DateTimeOffset now)
    {
        Body = RequireBody(body);
        UpdatedAt = now;
    }

    private static string RequireBody(string? body)
    {
        var value = body?.Trim();

        if (string.IsNullOrEmpty(value))
            throw new DomainException("Izoh matni bo'sh bo'lishi mumkin emas.");

        if (value.Length > MaxBodyLength)
            throw new DomainException($"Izoh {MaxBodyLength} belgidan oshmasin.");

        return value;
    }
}
