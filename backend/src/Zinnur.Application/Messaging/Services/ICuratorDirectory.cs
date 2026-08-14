using Zinnur.Domain.Entities;

namespace Zinnur.Application.Messaging.Services;

/// <summary>
/// ========================================================================
/// "KIM KIM BILAN BOG'LANGAN" — YAGONA QOIDA
/// ========================================================================
///
/// Xodim ↔ o'quvchi bog'lanishi IKKI yo'l bilan tuziladi va ikkalasi
/// ham teng kuchda (eski <c>dm_svc.py</c> dagi qoidaning aynan o'zi):
///
///   1) TO'G'RIDAN-TO'G'RI — ustoz guruhining <c>AssistantId</c> /
///      <c>TeacherId</c> si xodimga teng;
///   2) BOG'LANISH ORQALI — ustoz guruhining <c>CuratorGroupId</c> si
///      xodimga tegishli kurator guruhini ko'rsatadi.
///
/// ★ NIMA UCHUN ALOHIDA SERVIS: eski tizimda "Kuratorlik" bo'limi faqat
/// (2)-yo'lga tayanardi va bog'lanish qilinmagan markazlarda kuratorga
/// BO'SH ro'yxat ko'rsatardi — o'quvchilar savol yozardi, kurator esa
/// ularni umuman ko'rmasdi. Qoida bir necha joyda qo'lda takrorlangani
/// uchun tuzatish ham yarim qolgandi.
///
/// ════════════════════════════════════════════════════════════════════════
/// R40 (2026-08-14) — SUHBATDOSH ENDI BITTA EMAS
/// ════════════════════════════════════════════════════════════════════════
///
/// Ilgari bu servis o'quvchiga ANIQ BITTA xodim qaytarardi
/// (<c>ResolveCuratorAsync</c>) va u DOIM kurator o'rindig'idan olinardi —
/// ustoz o'rindig'iga umuman qaramasdi. Loyiha egasi esa savolga kim
/// javob berishini o'quv bo'limi TANLASHINI so'radi
/// (<c>Group.QuestionResponderRole</c>), va tanlovlardan biri —
/// "ikkalasi ham".
///
/// 🔴 SHU SABABLI RO'YXAT QAYTADI. Bitta o'quvchida ikki suhbatdosh
/// bo'lishi <c>DirectMessage</c> kalitini (<c>StudentId, StaffId</c>)
/// BUZMAYDI — aksincha, aynan o'sha kalit ikki suhbatni bir-biridan
/// AJRATIB turadi: ustoz kuratorning yozishmasini ko'ra olmaydi va
/// aksincha. Alohida "conversations" jadvali baribir KERAK EMAS.
///
/// ⚠️ QAYSI O'RINDIQ mas'ul ekanini bu servis O'ZI hal qilmaydi — qoida
/// <c>StaffResponsibility</c> da, chunki uni baholash servisi ham
/// o'qiydi (R33). Ikki mustaqil nusxa bo'lsa ular albatta ajralib
/// ketardi.
/// </summary>
public interface ICuratorDirectory
{
    /// <summary>
    /// O'quvchining savollariga javob beradigan xodim(lar), MAS'ULIYAT
    /// TARTIBIDA (birinchisi — asosiy suhbatdosh, u ro'yxat boshida
    /// ko'rsatiladi).
    ///
    /// Bo'sh ro'yxat — bu XATO EMAS (guruhga hali kurator biriktirilmagan
    /// bo'lishi mumkin). Frontend "Sizga hali kurator biriktirilmagan"
    /// deb ko'rsatadi.
    ///
    /// Faqat FAOL a'zolik, FAOL guruh, <see cref="Domain.Enums.GroupType.Group"/>
    /// turi va FAOL xodim hisobga olinadi.
    /// </summary>
    Task<IReadOnlyList<User>> ResolveRespondersAsync(
        long studentId, CancellationToken ct = default);

    /// <summary>
    /// Kurator NAZORATIDAGI ustoz guruhlari (faqat faol, faqat
    /// <see cref="Domain.Enums.GroupType.Group"/>). Kurator guruhining o'zi
    /// bu ro'yxatga KIRMAYDI — unda o'quvchi a'zo bo'lmaydi.
    ///
    /// ⚠️ BU METOD R40 DA TEGILMADI va ATAYLAB: uni GURUH CHATI o'qiydi
    /// (<c>GroupChatService</c>) — "kurator qaysi guruh chatlarini
    /// ko'radi" degan BOSHQA savol. Uni savollar sozlamasiga bog'lash
    /// hech kim so'ramagan yon ta'sir bo'lardi: o'quv bo'limi savollarni
    /// ustozga o'tkazgani zahoti kurator guruh chatlarini yo'qotardi.
    /// </summary>
    Task<IReadOnlyList<long>> ScopeGroupIdsAsync(long staffId, CancellationToken ct = default);

    /// <summary>
    /// Xodim SAVOLLARIGA mas'ul bo'lgan o'quvchilarning Id'lari (faol
    /// a'zolik). Ya'ni "kim menga yoza oladi" — shaxsiy yozishmaning
    /// ruxsat manbai.
    ///
    /// ⚠️ ENDI USTOZNI HAM QAYTARISHI MUMKIN — guruhda
    /// <c>QuestionResponderRole</c> shunday qo'yilgan bo'lsa. Standart
    /// qiymatda (<c>Assistant</c>) ro'yxat bugungidek: faqat kurator
    /// uchun to'la, ustoz uchun bo'sh.
    /// </summary>
    Task<IReadOnlyList<long>> StudentIdsAsync(long staffId, CancellationToken ct = default);
}
