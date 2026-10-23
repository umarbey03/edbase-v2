using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>
/// O'quvchi balli daftaridagi BITTA yozuv (2026-10-23).
///
/// ★ YOZUV — HOSILA MA'LUMOT: u qo'lda emas, manba jadvallaridan
///   (<c>Attendances</c>, <c>LessonGrades</c>, <c>Submissions</c>,
///   <c>TestAttempts</c>, <c>LessonProgress</c>) <c>PointsSyncService</c>
///   tomonidan yasaladi. Manba o'zgarsa (baho yoki davomat tuzatildi)
///   yozuv ham yangilanadi yoki o'chadi.
///
/// ★ <see cref="Ref"/> — O'QUVCHI ICHIDA TAKRORLANMAS kalit
///   (<c>att:{sessionId}</c>, <c>grade:{sessionId}</c>, ...). Unikal indeks
///   bitta hodisa ikki marta ball bermasligini bazaning o'zida kafolatlaydi.
///
/// ★ OY — <see cref="OccurredAt"/> bo'yicha (markaz vaqt zonasida):
///   davomat va baho uchun dars vaqti, vazifa uchun tekshirilgan, test
///   uchun topshirilgan, video uchun ko'rilgan vaqt.
/// </summary>
public class StudentPoint : BaseEntity
{
    public const int MaxRefLength = 64;

    public long StudentId { get; set; }

    public User? Student { get; set; }

    public PointKind Kind { get; set; }

    /// <summary>Ball o'zgarishi; kelmaganlik uchun manfiy.</summary>
    public int Delta { get; set; }

    public string Ref { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Darsga bog'liq yozuvlarda — dars (Telegram xabari va tarix uchun).</summary>
    public long? SessionId { get; set; }
}
