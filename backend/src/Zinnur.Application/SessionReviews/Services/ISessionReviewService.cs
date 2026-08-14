using Zinnur.Application.SessionReviews.Dtos;

namespace Zinnur.Application.SessionReviews.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// DARS SIFATI TAHLILI USE-CASE'LARI (talab R29 va R30)
/// ════════════════════════════════════════════════════════════════════════
///
/// R29 — o'quv bo'limi yozuvlar ro'yxatidagi videoga xulosa qo'yadi va uni
/// modal oynada o'qiydi. R30 — ustoz AYNI xulosani "Darslarim" bo'limidan,
/// AYNI shaklda ko'radi. Ikkalasi BITTA ma'lumot, ikkita oyna.
///
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 O'QUVCHI BU YERGA UMUMAN KIRA OLMAYDI — VA BU SERVISDA HAL QILINADI
/// ════════════════════════════════════════════════════════════════════════
///
/// ── NIMA UCHUN <c>ILiveSessionService.GetAsync</c> QAYTA ISHLATILMAYDI ──
///
/// <c>RecordingService</c> ning bosh qoidasi — "ruxsat bitta manbadan,
/// <c>ILiveSessionService</c> dan". BU YERDA U QOIDA ATAYLAB
/// QO'LLANILMAYDI va sabab aniq: <c>GetAsync</c> "SHU DARSNI ko'ra
/// olasanmi?" degan savolga javob beradi va guruhdagi HAR BIR FAOL
/// O'QUVCHI undan MUVAFFAQIYATLI o'tadi — o'z darsini ko'rish uning
/// haqqi. Bu servis esa BOSHQA savolni so'raydi: "o'quv bo'limining SHU
/// DARS HAQIDAGI ICHKI BAHOSINI ko'ra olasanmi?".
///
/// Ikki savolning auditoriyasi USTMA-UST TUSHMAYDI. Birinchisini qayta
/// ishlatish o'quvchiga ustozi haqidagi "Muammo bor" xulosasini ochib
/// berardi — bu esa ustozning ishonchini va o'quv bo'limining ochiq baho
/// yozish imkonini bir vaqtda yo'q qilardi.
///
/// ★ AYNI NAQSH ALLAQACHON LOYIHADA BOR: <c>StudentNoteService</c> ham
/// umumiy <c>StudentAccess</c> darvozasidan o'tgach, <c>Student</c> uchun
/// QO'SHIMCHA 403 qo'yadi. Ya'ni bu istisno emas, uslub.
///
/// 🔴 RAD ETISH TUGMANI YASHIRISH BILAN EMAS. <c>SessionReviewDto.CanEdit</c>
/// va frontenddagi shartlar — QULAYLIK. Yagona haqiqiy chegara — shu
/// servisning birinchi qatoridagi rol tekshiruvi, va u HAR metodda
/// takrorlanadi (yagona umumiy `Authorize` ichida).
///
/// ── KIM NIMA QILA OLADI ─────────────────────────────────────────────────
///
/// | Rol                      | O'qish                    | Yozish |
/// |--------------------------|---------------------------|--------|
/// | <c>Admin</c>/<c>Academic</c> | HAR QANDAY dars        | ✅     |
/// | <c>Teacher</c>/<c>Assistant</c> | FAQAT o'z guruhi darsi | ❌  |
/// | <c>Student</c>           | ❌ (403)                  | ❌     |
///
/// ⚠️ Ustoz o'z darsining tahlilini TAHRIRLAY OLMAYDI — u sifat nazorati
/// obyekti, sub'ekti emas. Aks holda "Muammo bor" xulosasini o'zi
/// "Tasdiqlandi" ga o'zgartirib qo'ya olardi va butun R29 ma'nosiz
/// bo'lardi.
/// </summary>
public interface ISessionReviewService
{
    /// <summary>
    /// Darsning tahlili. <c>null</c> — hali yozilmagan.
    ///
    /// ★ NIMA UCHUN <c>null</c>, 404 EMAS: "tahlil hali yo'q" — NORMAL va
    /// eng ko'p uchraydigan holat, xato emas. 404 bo'lsa klient uni xato
    /// yo'lida ushlab, "dars topilmadi" bilan aralashtirib yuborardi
    /// (ikkalasi ham 404 bo'lardi), modal esa har ochilishida qizil
    /// ogohlantirish ko'rsatardi.
    /// </summary>
    Task<SessionReviewDto?> GetAsync(long sessionId, long actorId, CancellationToken ct = default);

    /// <summary>
    /// Tahlilni yozadi yoki mavjudini yangilaydi (faqat o'quv bo'limi/admin).
    /// </summary>
    Task<SessionReviewDto> SaveAsync(
        long sessionId, SaveSessionReviewRequest request, long actorId,
        CancellationToken ct = default);

    /// <summary>
    /// Tahlilni o'chiradi (faqat o'quv bo'limi/admin).
    ///
    /// ⚠️ QATTIQ o'chirish: xulosa xodimning ish yozuvi va unga havola
    /// qiladigan boshqa qator yo'q (<c>StudentNoteService.DeleteAsync</c>
    /// dagi AYNI mulohaza). Tahlil bo'lmagan darsda metod JIM o'tadi —
    /// "o'chirish" idempotent amal.
    /// </summary>
    Task DeleteAsync(long sessionId, long actorId, CancellationToken ct = default);
}
