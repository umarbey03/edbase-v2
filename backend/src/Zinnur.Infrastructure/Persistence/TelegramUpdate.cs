namespace Zinnur.Infrastructure.Persistence;

/// <summary>
/// Ishlangan Telegram yangilanishining izi (idempotentlik jurnali).
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★ NIMA UCHUN JADVAL KERAK
///
/// Telegram webhook'dan 200 javobini kutadi. Javob kechiksa yoki tarmoq
/// uzilsa, u AYNI yangilanishni QAYTA yuboradi. Bu jadvalsiz bitta
/// "raqamni ulashish" hodisasi ikki marta ishlanardi: o'quvchi ikki xil
/// javob olardi, navbatga esa takroriy xabar tushardi.
///
/// ★ NIMA UCHUN XOTIRADA (kesh) EMAS: API ikki konteynerda ishlaydi va
/// takror yangilanish IKKINCHI instansiyaga tushishi mumkin. Jarayon
/// xotirasidagi ro'yxat uni ko'rmasdi. Redis ham bo'lardi, lekin unda
/// yozuv bog'lash bilan BITTA tranzaksiyada saqlanmasdi — "belgilandi,
/// lekin bog'lanmadi" holati paydo bo'lardi.
///
/// ★ NIMA UCHUN DOMAIN ENTITY EMAS (<c>MessageOutbox</c> bilan ayni sabab):
/// bu biznes tushunchasi emas, YETKAZIB BERISH mexanizmi. Shuning uchun
/// sinf Infrastructure ichida qoladi va <c>IApplicationDbContext</c> da
/// OCHILMAYDI — use-case'lar unga <c>ITelegramUpdateLog</c> porti orqali
/// tegadi.
///
/// ★ NIMA UCHUN <c>BaseEntity</c> EMAS: kalit BIZDA yaratilmaydi, uni
/// Telegram beradi. <c>BaseEntity.Id</c> — identity ustun; ikkinchi sun'iy
/// kalit qo'shish faqat chalkashlik va ortiqcha indeks bo'lardi.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class TelegramUpdate
{
    /// <summary>Telegram bergan o'suvchi raqam — BIRLAMCHI KALIT.</summary>
    public long UpdateId { get; set; }

    /// <summary>
    /// Qachon qabul qilingan. Faqat TOZALASH uchun: jadval cheksiz o'smasin
    /// (eski qatorlarni davriy o'chirish rejaga kiritilgan).
    /// </summary>
    public DateTimeOffset ReceivedAt { get; set; }
}
