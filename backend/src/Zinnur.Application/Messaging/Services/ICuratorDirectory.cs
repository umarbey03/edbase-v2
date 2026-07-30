using Zinnur.Domain.Entities;

namespace Zinnur.Application.Messaging.Services;

/// <summary>
/// ========================================================================
/// "KIM KIM BILAN BOG'LANGAN" — YAGONA QOIDA
/// ========================================================================
///
/// Kurator ↔ o'quvchi bog'lanishi IKKI yo'l bilan tuziladi va ikkalasi
/// ham teng kuchda (eski <c>dm_svc.py</c> dagi qoidaning aynan o'zi):
///
///   1) TO'G'RIDAN-TO'G'RI — ustoz guruhining <c>AssistantId</c> si
///      kuratorga teng;
///   2) BOG'LANISH ORQALI — ustoz guruhining <c>CuratorGroupId</c> si
///      kuratorga tegishli kurator guruhini ko'rsatadi.
///
/// ★ NIMA UCHUN ALOHIDA SERVIS: eski tizimda "Kuratorlik" bo'limi faqat
/// (2)-yo'lga tayanardi va bog'lanish qilinmagan markazlarda kuratorga
/// BO'SH ro'yxat ko'rsatardi — o'quvchilar savol yozardi, kurator esa
/// ularni umuman ko'rmasdi. Qoida bir necha joyda qo'lda takrorlangani
/// uchun tuzatish ham yarim qolgandi. Endi qoida BITTA joyda: yozishma,
/// kelajakdagi kurator paneli va davomat nazorati shu servisga tayanadi.
/// </summary>
public interface ICuratorDirectory
{
    /// <summary>
    /// O'quvchining kuratori. Topilmasa <c>null</c> — bu XATO EMAS
    /// (guruhga hali kurator biriktirilmagan bo'lishi mumkin).
    ///
    /// Faqat FAOL a'zolik, FAOL guruh, <see cref="Domain.Enums.GroupType.Group"/>
    /// turi va FAOL kurator hisobga olinadi.
    /// </summary>
    Task<User?> ResolveCuratorAsync(long studentId, CancellationToken ct = default);

    /// <summary>
    /// Kurator NAZORATIDAGI ustoz guruhlari (faqat faol, faqat
    /// <see cref="Domain.Enums.GroupType.Group"/>). Kurator guruhining o'zi
    /// bu ro'yxatga KIRMAYDI — unda o'quvchi a'zo bo'lmaydi.
    /// </summary>
    Task<IReadOnlyList<long>> ScopeGroupIdsAsync(long staffId, CancellationToken ct = default);

    /// <summary>Kurator mas'ul bo'lgan o'quvchilarning Id'lari (faol a'zolik).</summary>
    Task<IReadOnlyList<long>> StudentIdsAsync(long staffId, CancellationToken ct = default);
}
