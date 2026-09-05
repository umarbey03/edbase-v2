using Zinnur.Domain.Enums;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TUNGI YIG'UVCHI PORTI — xom bo'laklardan BITTA mp4
/// ════════════════════════════════════════════════════════════════════════
///
/// Application qatlami na <c>Process</c> ni, na ffmpeg argumentlarini, na
/// fayl tizimini ko'radi: amalga oshirilishi Infrastructure'da
/// (<c>FfmpegRecordingComposer</c>).
///
/// ── 🔴 REJANI BU PORT TUZMAYDI ──────────────────────────────────────────
///
/// Eng qiyin qism — VAQT O'QI va undan kelib chiqadigan filtr grafi —
/// <see cref="RecordingCompositionPlanner"/> da, ya'ni SOF funksiyada:
/// kirishi baza qatorlari, chiqishi satr. Shu tufayli "ekran ulashish
/// dars o'rtasida yoqildi va ustoz bir marta uzildi" degan holat
/// PROTSESSSIZ, TARMOQSIZ VA FAYLSIZ tekshiriladi
/// (<c>RecordingCompositionPlannerTests</c>).
///
/// Agar filtr grafi shu portning ORTIDA qurilsa, uni tekshirishning
/// yagona yo'li 90 daqiqalik kodlashni yurgizish bo'lardi — ya'ni amalda
/// hech qachon tekshirilmasdi.
///
/// ── PORT NIMANI O'Z ZIMMASIGA OLADI ─────────────────────────────────────
///
///   1) ishchi papka (scratch) — ochish va HAR HOLDA o'chirish;
///   2) xom obyektlarni ombordan DISKKA tushirish;
///   3) <c>ffprobe</c> bilan har faylning haqiqiy uzunligini o'lchash;
///   4) <c>ffmpeg</c> ni rejadagi graf bilan yurgizish;
///   5) natijani tekshirish (bitta video + bitta ovoz oqimi, uzunlik);
///   6) tayyor faylni rejadagi kalitga YUKLASH.
///
/// ⚠️ BAZAGA TEGMAYDI. Qator holati, urinishlar hisobi va xom fayllarni
/// tozalash — chaqiruvchining (Application) ishi. Shuning uchun o'lchangan
/// uzunliklar natija ichida QAYTARILADI
/// (<see cref="CompositionResult.Probes"/>), yozilmaydi.
/// </summary>
public interface IRecordingComposer
{
    /// <summary>
    /// Rejani bajaradi: yuklab oladi, o'lchaydi, kodlaydi, tekshiradi va
    /// omborga qo'yadi.
    ///
    /// 🔴 ISTISNO TASHLAMAYDI (bekor qilishdan tashqari): har qanday
    /// nosozlik <see cref="CompositionResult.Fail"/> bo'lib qaytadi va
    /// chaqiruvchi undan urinishlar hisobini yuritadi. Istisno bo'lsa
    /// qator <c>Running</c> holida osilib qolardi va uni faqat ijara
    /// muddati tugagach boshqa ishchi ko'tarardi.
    ///
    /// ⚠️ <see cref="OperationCanceledException"/> ESA TASHLANADI VA BU
    /// ATAYLAB: bekor qilish — nosozlik EMAS, tungi oynaning tugashi.
    /// Chaqiruvchi uni <c>InterruptComposition</c> ga aylantiradi, ya'ni
    /// urinish sarflanmaydi.
    ///
    /// ⚠️ YARIM QOLGAN NATIJA HECH QACHON YUKLANMAYDI: yuklash — oxirgi
    /// qadam va u faqat tekshiruvdan o'tgan fayl uchun bajariladi.
    /// </summary>
    Task<CompositionResult> ComposeAsync(CompositionPlan plan, CancellationToken ct = default);

    /// <summary>
    /// Ishchi papkadan ESKI qoldiqlarni o'chiradi (ishga tushishda).
    ///
    /// ★ NIMA UCHUN KERAK: papka odatda <c>finally</c> da o'chiriladi,
    /// lekin konteyner OOM bilan o'ldirilsa yoki xost qayta yuklansa
    /// <c>finally</c> UMUMAN bajarilmaydi. Bir necha bunday hodisadan
    /// keyin 6 GB lik qoldiqlar diskni to'ldirardi va keyingi kodlash
    /// "no space left" bilan yiqilardi — ya'ni bitta unutilgan papka
    /// butun tungi navbatni to'xtatardi.
    ///
    /// ⚠️ YOSHI BO'YICHA, RO'YXAT BO'YICHA EMAS: hozir ishlayotgan
    /// yig'ishning papkasiga tegib qo'ymaslik uchun. Shuning uchun
    /// <paramref name="maxAge"/> eng uzun kodlashdan sezilarli katta
    /// bo'lishi SHART.
    /// </summary>
    /// <returns>O'chirilgan papkalar soni.</returns>
    Task<int> CleanScratchAsync(TimeSpan maxAge, CancellationToken ct = default);
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// YIG'ISH REJASI — bazadan hisoblangan, ffmpeg uchun TAYYOR ko'rsatma
/// ════════════════════════════════════════════════════════════════════════
///
/// Reja SOF ma'lumot: uni <see cref="RecordingCompositionPlanner"/>
/// yasaydi va u yasalgandan keyin hech narsa unga qarab qaror qabul
/// qilmaydi — Infrastructure faqat IJRO etadi.
/// </summary>
/// <param name="RecordingId">Qaysi yozuv (ishchi papka nomi ham shundan).</param>
/// <param name="TargetObjectKey">
/// Tayyor mp4 AYNAN shu kalitga qo'yiladi.
///
/// ★ KALIT YANGI EMAS: u dars boshlanganda yaratilgan va o'quvchining
/// havolasi ham o'shanga qarab beriladi (§4.5-7).
/// </param>
/// <param name="Inputs">
/// ffmpeg kirishlari — <c>-i</c> lar AYNAN shu tartibda beriladi, chunki
/// filtr grafidagi <c>[0:v]</c>, <c>[1:v]</c>, <c>[2:a]</c> yorliqlari
/// shu tartibga bog'langan.
/// </param>
/// <param name="FilterGraph">
/// <c>-filter_complex</c> ning to'liq qiymati. Chiqish yorliqlari
/// <see cref="VideoLabel"/> va <see cref="AudioLabel"/> — ular DOIM
/// mavjud (ovozsiz dars ham jimlik oqimini oladi, tasvirsiz dars ham
/// qora fonni), chunki ikkala oqimsiz fayl brauzer pleyerida yomon
/// ochiladi (§4.6).
/// </param>
/// <param name="TimelineSeconds">
/// Vaqt o'qining to'liq uzunligi: <c>MAX(EndedAt) - T0</c>. Tekshiruv
/// natijani AYNAN shu qiymat bilan solishtiradi (±2 s).
/// </param>
/// <param name="Preset">x264 preseti (<c>recordings.compose_preset</c>).</param>
/// <param name="Crf">x264 CRF (<c>recordings.compose_crf</c>).</param>
/// <param name="Warning">
/// Xodimga ko'rsatiladigan ogohlantirish yoki <c>null</c>. Bugungi
/// yagona holat — "ovoz yo'q": fayl jimlik bilan chiqadi va buni
/// ochmasdan bilish kerak (§4.6).
/// </param>
public sealed record CompositionPlan(
    long RecordingId,
    string TargetObjectKey,
    IReadOnlyList<CompositionInput> Inputs,
    string FilterGraph,
    double TimelineSeconds,
    string Preset,
    int Crf,
    string? Warning)
{
    /// <summary>Filtr grafidagi tayyor TASVIR oqimining yorlig'i.</summary>
    public const string VideoLabel = "[v]";

    /// <summary>Filtr grafidagi tayyor OVOZ oqimining yorlig'i.</summary>
    public const string AudioLabel = "[a]";
}

/// <summary>
/// Bitta ffmpeg kirishi = bitta xom fayl.
/// </summary>
/// <param name="Index">
/// <c>-i</c> larning tartibi. Filtr grafidagi <c>[N:v]</c> / <c>[N:a]</c>
/// AYNAN shu raqam — ikkalasi bir manbadan (rejadan) chiqadi.
/// </param>
/// <param name="TrackId">
/// Qaysi <c>RecordingTrack</c> qatoridan kelgani — o'lchangan uzunlik
/// (<c>ProbedDurationMs</c>) shu bo'yicha qaytariladi.
/// </param>
/// <param name="Kind">Bo'lak turi (log va tashxis uchun).</param>
/// <param name="ObjectKey">Ombordagi XOM kalit — shundan yuklab olinadi.</param>
/// <param name="FileName">
/// Ishchi papkadagi fayl nomi. Rejada turishi ATAYLAB: nom KUTILGAN
/// bo'lsa, yiqilgan kodlashning log qatorini o'sha papkadagi fayl bilan
/// solishtirish mumkin.
/// </param>
/// <param name="ItsOffsetSeconds">
/// <c>-itsoffset</c> qiymati — bo'lakning vaqt o'qidagi o'rni.
///
/// 🔴 OVOZ KIRISHLARIDA DOIM <c>0</c>. Ovozning o'rni filtr grafi ICHIDA
/// (<c>adelay</c>) beriladi, chunki unga kalibrlash konstantasi
/// (<c>recordings.compose_audio_offset_ms</c>) ham qo'shiladi va u MANFIY
/// bo'lishi mumkin — <c>-itsoffset</c> esa manfiy qiymatda vaqt
/// belgilarini nolgacha qirqib tashlaydi. Ikkala joyda ham surish
/// berilsa, ovoz IKKI BAROBAR siljirdi.
/// </param>
/// <param name="ExpectedDurationMs">
/// LiveKit vaqtlaridan hisoblangan uzunlik (<c>EndedAt - StartedAt</c>).
/// O'lchangan uzunlik bundan 2 soniyadan ko'p farq qilsa — siljish
/// signali (§9.1).
/// </param>
public sealed record CompositionInput(
    int Index,
    long TrackId,
    RecordingTrackKind Kind,
    string ObjectKey,
    string FileName,
    double ItsOffsetSeconds,
    int ExpectedDurationMs);

/// <summary>
/// Yig'ishning natijasi.
///
/// ★ ISTISNO O'RNIGA NATIJA — <c>EgressStartResult</c> dagi AYNI
/// mulohaza: nosozlik BOSHQARILADIGAN hol bo'lishi kerak, chunki undan
/// keyin urinishlar hisobi va o'zbekcha sabab yoziladi.
/// </summary>
/// <param name="Succeeded">Tayyor fayl omborga tushdimi.</param>
/// <param name="SizeBytes">Tayyor faylning hajmi.</param>
/// <param name="DurationSeconds">Tayyor videoning O'LCHANGAN uzunligi.</param>
/// <param name="Error">Nima uchun chiqmagani — XODIM uchun, o'zbekcha.</param>
/// <param name="Probes">
/// Har xom faylning <c>ffprobe</c> bilan o'lchangan uzunligi.
///
/// ★ NIMA UCHUN NATIJADA QAYTADI, BAZAGA YOZILMAYDI: port bazani
/// ko'rmaydi. Chaqiruvchi bu qiymatlarni <c>ProbedDurationMs</c> ga
/// yozadi va kutilgan uzunlik bilan solishtiradi — o'sha farq A/V
/// siljishining yagona avtomatik o'lchovi (§9.1-1).
///
/// ⚠️ YIQILGAN NATIJADA HAM TO'LDIRILGAN BO'LISHI MUMKIN: o'lchash
/// kodlashdan OLDIN bo'ladi, ya'ni kodlash yiqilsa ham o'lchovlar
/// qimmatli.
/// </param>
/// <param name="MissingTrackIds">
/// Ombordan TOPILMAGAN xom bo'laklar.
///
/// 🔴 NIMA UCHUN ALOHIDA RO'YXAT, ODDIY XATO EMAS: qator <c>Completed</c>
/// bo'lgan, ya'ni fayl bir paytlar bor edi. Endi yo'q bo'lsa — bu bitta
/// bo'lakning yo'qolishi, BUTUN DARSNING emas. Chaqiruvchi o'sha
/// qatorlarni <c>Failed</c> deb belgilaydi va keyingi urinishda reja
/// ularsiz quriladi: dars 90 daqiqasidan besh daqiqasini yo'qotadi,
/// hammasini emas. Ro'yxat bo'lmasa yagona iloj butun yozuvni yiqitish
/// bo'lardi va u har urinishda AYNAN shu joyda qayta yiqilardi.
/// </param>
public sealed record CompositionResult(
    bool Succeeded,
    long? SizeBytes,
    int? DurationSeconds,
    string? Error,
    IReadOnlyList<ProbedTrackDuration> Probes,
    IReadOnlyList<long> MissingTrackIds)
{
    public static CompositionResult Ok(
        long sizeBytes,
        int durationSeconds,
        IReadOnlyList<ProbedTrackDuration> probes) =>
        new(true, sizeBytes, durationSeconds, null, probes, []);

    public static CompositionResult Fail(
        string error,
        IReadOnlyList<ProbedTrackDuration>? probes = null,
        IReadOnlyList<long>? missingTrackIds = null) =>
        new(false, null, null, error, probes ?? [], missingTrackIds ?? []);
}

/// <summary>Bitta xom faylning o'lchangan uzunligi.</summary>
/// <param name="TrackId">Qaysi bo'lak qatori.</param>
/// <param name="DurationMs">Faylning HAQIQIY uzunligi (millisekund).</param>
public sealed record ProbedTrackDuration(long TrackId, int DurationMs);
