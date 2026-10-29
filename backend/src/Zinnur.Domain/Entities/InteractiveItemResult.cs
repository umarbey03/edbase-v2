using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// O'quvchi × mashq natijasi (2026-10-29).
///
/// ★ BIRINCHI URINISH HISOBGA OLINADI: <see cref="FirstRight"/> /
///   <see cref="FirstTotal"/> mashqning HAR savoliga birinchi marta berilgan
///   javobdan bir marta yig'iladi va keyin o'zgarmaydi. Qayta mashq qilish
///   mumkin — u faqat <see cref="Attempts"/> va <c>Last*</c> ni yangilaydi.
///
/// ★ <see cref="State"/> — birinchi urinish davomidagi holat (`jsonb`):
///   qaysi savolga javob berildi, `ula` dagi xatolar soni va h.k. Shakli
///   mashq turiga bog'liq va faqat `InteractiveProgressService` biladi.
///
/// 🔴 Parallel ikki so'rovda ham birinchi urinish bir marta yozilsin: qator
///   `(StudentId, UnitId, ItemKey)` bo'yicha noyob va `xmin` optimistik qulfi bilan.
/// </summary>
public class InteractiveItemResult : BaseEntity
{
    public const int MaxItemKeyLength = 64;

    public long StudentId { get; set; }

    public long UnitId { get; set; }

    public long VersionId { get; set; }

    /// <summary>Mashq id'si ("A1m1"), tovush testi ("A4:test") yoki daftar ("W3").</summary>
    public required string ItemKey { get; set; }

    /// <summary>`jsonb`: birinchi urinish holati.</summary>
    public string State { get; set; } = "{}";

    public int? FirstRight { get; set; }

    public int? FirstTotal { get; set; }

    public DateTimeOffset? FirstAt { get; set; }

    public int Attempts { get; set; }

    public int? LastRight { get; set; }

    public DateTimeOffset? LastAt { get; set; }

    public bool IsFirstDone => FirstTotal is not null;
}
