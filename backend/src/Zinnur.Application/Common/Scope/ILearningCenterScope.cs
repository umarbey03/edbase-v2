namespace Zinnur.Application.Common.Scope;

/// <summary>
/// ========================================================================
/// O'QUV MARKAZ QAMROVI — KO'P-MARKAZLI KELAJAK UCHUN YAGONA CHOK (SEAM)
/// ========================================================================
///
/// ── NIMA UCHUN BU INTERFEYS UMUMAN BOR ─────────────────────────────────
///
/// Egasining qarori: reyting IKKI qamrovda bo'ladi — guruh bo'yicha va
/// BUTUN O'QUV MARKAZ bo'yicha. Va ayni qaror bilan birga ikkinchi shart
/// keldi: *"biz bu loyihani kengaytirib bir nechta o'quv markazlar
/// sotishimizni hisobga olganda umumiy rating faqat o'quv markaz uchun
/// amal qilishi kerak, ya'ni jami tizim foydalanuvchilari uchun emas"*.
///
/// 🔴 BUGUNGI HAQIQAT: KODDA `LearningCenter` (tenant / tashkilot)
///    TUSHUNCHASI UMUMAN YO'Q. Bitta deployment = bitta o'quv markaz,
///    ya'ni "markaz bo'yicha" va "tizimdagi hamma o'quvchi" BUGUN AYNAN
///    BIR XIL to'plam.
///
/// ★ SHUNING UCHUN QAMROV KODDA NOMLANDI, GARCHI U BUGUN HECH NARSANI
///   FILTRLAMASA HAM. Agar reyting servisi to'g'ridan-to'g'ri
///   `db.Users.Where(u => u.Role == Student)` deb yozganda edi, ertaga
///   `LearningCenter` qo'shilganda uni topish uchun butun kodni titkilash
///   kerak bo'lardi — va bitta unutilgan joy BOSHQA MARKAZNING
///   o'quvchilarini begona reytingga chiqarib qo'yardi. Bu shunchaki xato
///   emas, MIJOZLAR ORASIDA MA'LUMOT SIZIB CHIQISHI bo'lardi.
///
///   Bu yerda esa savol BITTA joyda so'raladi: "ko'ruvchining markaziga
///   qaysi o'quvchilar kiradi?".
///
/// ── KELAJAKDAGI O'ZGARISH AYNAN QAYERGA TUSHADI ────────────────────────
///
/// `LearningCenter` entity'si paydo bo'lganda (bu ALOHIDA qaror, egasi
/// hali qabul qilmagan — shuning uchun bu yerda entity YARATILMAYDI):
///
///   1. `User` (yoki `Group`) ga `LearningCenterId` ustuni qo'shiladi;
///   2. <see cref="SingleCenterScope"/> ning ICHI almashtiriladi —
///      ko'ruvchining `LearningCenterId` si o'qiladi va uchala so'rovga
///      `WHERE learning_center_id = @id` qo'shiladi;
///   3. <see cref="LearningCenterAudience.CacheDiscriminator"/> `"solo"`
///      o'rniga markaz Id'sini qaytaradi — kesh kalitlari o'z-o'zidan
///      markazlarga bo'linadi (kalit dizayni shuning uchun shunday).
///
/// CHAQIRUVCHILARNING BIRORTASI O'ZGARMAYDI. Bugungi kunda seam'ni
/// ishlatadigan yagona joy — `LeaderboardService` (markaz reytingi).
/// </summary>
public interface ILearningCenterScope
{
    /// <summary>
    /// Ko'ruvchining o'quv markazidagi reytingga kiradigan o'quvchilar.
    ///
    /// ★ RUXSAT SHU YERDA HAL BO'LADI: ko'ruvchi markazga tegishli
    /// bo'lmasa (bugun — profili faol bo'lmasa) istisno ko'tariladi.
    /// Reyting servisi ruxsat qoidasini TAKRORLAMAYDI.
    /// </summary>
    /// <exception cref="Zinnur.Application.Common.Exceptions.NotFoundException">
    /// Ko'ruvchi topilmadi.
    /// </exception>
    /// <exception cref="Zinnur.Application.Common.Exceptions.ForbiddenException">
    /// Ko'ruvchi profili faol emas (kelajakda: markazga tegishli emas).
    /// </exception>
    Task<LearningCenterAudience> ResolveForViewerAsync(
        long viewerId, CancellationToken ct = default);
}

/// <summary>
/// Bitta o'quv markazning "reyting auditoriyasi" — qamrov javobining
/// TO'LIQ shakli.
/// </summary>
/// <param name="CacheDiscriminator">
/// Kesh kalitiga qo'shiladigan markaz belgisi. Bugun — <c>"solo"</c>
/// (bitta deployment = bitta markaz).
///
/// 🔴 BU MAYDON KESH XAVFSIZLIGINING O'ZAGI. <c>ICacheService</c> da
/// prefiks bo'yicha o'chirish YO'Q (faqat TTL), ya'ni noto'g'ri kalit
/// bilan yozilgan jadvalni qo'lda tozalab bo'lmaydi — u TTL tugagunicha
/// begona markazga ko'rinib turardi. Shuning uchun markaz belgisi
/// kalitning ICHIDA, qo'shimcha sifatida emas.
/// </param>
/// <param name="Students">
/// Markazdagi reytingga kiradigan o'quvchilar (faol profil).
/// ★ Tartib kafolatlanmaydi — reyting baribir ball bo'yicha qayta tartiblanadi.
/// </param>
/// <param name="GroupIds">
/// Markazdagi FAOL guruhlarning Id'lari — o'quvchilarning a'zoliklaridan
/// olinadi (qo'shimcha so'rovsiz).
///
/// ★ NIMA UCHUN AUDITORIYA ICHIDA: markaz reytingida vazifa va davomat
/// so'rovlari ham markaz bilan chegaralanishi kerak. Agar guruh Id'lari
/// shu yerdan kelmasa, ertangi ko'p-markazli o'zgarish UCHTA joyni
/// tuzatishni talab qilardi — chok esa BITTA bo'lishi kerak.
/// </param>
public sealed record LearningCenterAudience(
    string CacheDiscriminator,
    IReadOnlyList<CenterStudent> Students,
    IReadOnlyList<long> GroupIds);

/// <summary>
/// Markaz reytingidagi bitta o'quvchi.
/// </summary>
/// <param name="StudentId">O'quvchi.</param>
/// <param name="FullName">Ko'rinadigan ism.</param>
/// <param name="PrimaryGroupId">
/// ASOSIY guruh — davomat MAXRAJI shu guruhdan olinadi.
///
/// ★ NIMA UCHUN QAMROV JAVOBIDA GURUH BOR: davomat foizi "qatnashgan
/// darslar / O'TILGAN darslar" va maxraj HAR O'QUVCHIDA O'ZINIKI —
/// batafsil <c>LeaderboardService.ComputeCenterAsync</c> izohida.
///
/// <c>null</c> — o'quvchi hech qaysi faol guruhda emas: u reytingda
/// qoladi (vazifa va test ballari bor bo'lishi mumkin), lekin davomat
/// mezoni "ma'lumot yo'q" bo'ladi.
/// </param>
public sealed record CenterStudent(long StudentId, string FullName, long? PrimaryGroupId);
