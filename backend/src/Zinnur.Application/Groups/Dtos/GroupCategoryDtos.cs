namespace Zinnur.Application.Groups.Dtos;

// ============================================================================
// GURUH KATEGORIYASI DTO'LARI (R21b)
//
// Lug'at jadvali — o'quv bo'limi paneldan boshqaradi. Shakl `CourseDtos`
// dagi kurs DTO'lari bilan ATAYLAB bir xil naqshda (nom + faollik + tartib
// + sanoq), chunki ikkalasi ham bir xil boshqaruv ekranida ishlatiladi va
// bir xil ko'rinishga ega bo'lishi kerak.
// ============================================================================

/// <summary>Kategoriya — ro'yxat va tanlagich uchun YAGONA shakl.</summary>
/// <param name="GroupCount">
/// Shu kategoriyaga biriktirilgan guruhlar soni (bazada sanaladi — N+1 yo'q).
///
/// ★ NIMA UCHUN KERAK: kategoriyani o'chirishga urinishdan OLDIN nechta guruh
/// yorlig'ini yo'qotishi ko'rinib tursin. FK <c>SET NULL</c> bo'lgani uchun
/// o'chirish JIMGINA muvaffaqiyatli tugaydi va guruhlar yorliqsiz qoladi —
/// ya'ni bu sonni ko'rsatmaslik ma'lumot yo'qotishning eng oson yo'li edi.
/// </param>
public sealed record GroupCategoryDto(
    long Id,
    string Name,
    int Position,
    bool IsActive,
    int GroupCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Ro'yxat filtri.
///
/// ★ SAHIFALASH YO'Q — <c>CourseListQuery</c> dan ATAYLAB farq qiladi.
/// Lug'at o'nlab qatordan iborat (markazda 4–10 yo'nalish) va u HAR BIR
/// guruh formasida, guruhlar ro'yxatida va chatlar ro'yxatida TANLAGICH
/// sifatida to'liq kerak bo'ladi. Sahifalangan bo'lsa har chaqiruv joyi
/// "hammasini olish uchun pageSize nechta bo'lsin?" degan savolga o'zicha
/// javob berardi va 26-kategoriya jimgina tanlagichdan tushib qolardi.
/// </summary>
/// <param name="IsActive">
/// <c>true</c> — faqat faollar (yangi guruhga tanlash uchun). <c>null</c> —
/// hammasi (boshqaruv ekrani arxivlanganlarni ham ko'rsatadi).
/// </param>
public sealed record GroupCategoryListQuery(bool? IsActive = null);

/// <summary>
/// Yangi kategoriya. <c>Position</c> SO'RALMAYDI — u oxiriga qo'shiladi
/// (<c>CreateCourseRequest</c> bilan AYNI kelishuv).
/// </summary>
public sealed record CreateGroupCategoryRequest(string Name, bool IsActive = true);

/// <summary>
/// Tahrirlash — TO'LIQ shakl (PUT semantikasi). <c>Position</c> bu yerda ham
/// YO'Q: tartib alohida "reorder" amalining ishi bo'lardi, hozircha u
/// yaratish tartibida qoladi.
/// </summary>
public sealed record UpdateGroupCategoryRequest(string Name, bool IsActive = true);
