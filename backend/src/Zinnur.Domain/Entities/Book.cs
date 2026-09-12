using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// KUTUBXONA KITOBI — PDF (2026-09-09).
///
/// NIMA UCHUN KERAK: telefondan dars o'tayotgan ustoz EKRAN ULASHA OLMAYDI
/// (mobil brauzerda `getDisplayMedia` yo'q). Kitoblar platformaga PDF
/// sifatida yuklanadi; jonli darsda ustoz kitob va sahifani tanlaydi,
/// sahifa uning qurilmasida chiziladi (chizma/belgi bilan) va LiveKit'ga
/// ekran ulashuvi treki sifatida uzatiladi.
///
/// ★ FAYLNING O'ZI OMBORDA (<c>IMediaStorage</c>, papka <c>books</c>) —
/// dars fayllari (<c>LessonAsset</c>) bilan AYNI mexanizm. Bu yerda faqat
/// kalit va tavsif.
///
/// ★ KURSGA BOG'LANMAGAN: kitob bir necha kursda (fonetika, grammatika)
/// ishlatiladi. Kerak bo'lsa keyin <c>CourseId</c> qo'shiladi.
/// </summary>
public class Book : BaseEntity
{
    public const int MaxTitleLength = 200;

    public required string Title { get; set; }

    /// <summary>Ombor kaliti (<c>books/&lt;guid&gt;.pdf</c>).</summary>
    public required string ObjectKey { get; set; }

    /// <summary>Hozircha faqat <c>application/pdf</c> — mazmundan aniqlanadi.</summary>
    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Sahifalar soni — KLIENT aytadi (PDF'ni brauzer o'qiydi). <c>null</c> — noma'lum.</summary>
    public int? PageCount { get; set; }

    public long CreatedById { get; set; }

    /// <summary>Arxivlangan kitob ro'yxatda chiqmaydi, fayl o'chirilmaydi.</summary>
    public bool IsActive { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
            throw new DomainException("Kitob nomi kiritilishi shart.");

        if (Title.Length > MaxTitleLength)
            throw new DomainException($"Kitob nomi {MaxTitleLength} belgidan oshmasin.");

        if (string.IsNullOrWhiteSpace(ObjectKey))
            throw new DomainException("Ombor kaliti bo'sh bo'lishi mumkin emas.");

        if (string.IsNullOrWhiteSpace(ContentType))
            throw new DomainException("Fayl turi (MIME) aniqlanmagan.");

        if (SizeBytes <= 0)
            throw new DomainException("Fayl hajmi noldan katta bo'lishi kerak.");

        if (PageCount is { } pages && pages is < 1 or > 10_000)
            throw new DomainException("Sahifalar soni haqiqatga to'g'ri kelmaydi.");
    }
}
