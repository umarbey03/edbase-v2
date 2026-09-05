using Microsoft.Extensions.Logging;

namespace Zinnur.Application.Recordings;

/// <summary>
/// Yozuv modulining manba-generatsiyali log metodlari.
///
/// ★ NIMA UCHUN ALOHIDA SINF: oddiy <c>logger.LogInformation("…", a, b)</c>
/// har chaqiruvda <c>object[]</c> ajratadi va qiymatlarni bokslaydi
/// (CA1848). Bundan tashqari modulning barcha xabarlari va ularning
/// EventId'lari BIR JOYDA turadi — <c>ApiLog</c> ga tegilmaydi.
///
/// EventId makoni: <c>6500–6529</c> (yozuv), <c>6530–6535</c> (trek
/// webhook'i), <c>6536–6549</c> (trek moslashtiruvchisi),
/// <c>6550–6559</c> (watchdog), <c>6560–6579</c> (tungi yig'ish).
/// </summary>
internal static partial class RecordingLog
{
    // ================================================================= webhook

    [LoggerMessage(
        EventId = 6500,
        Level = LogLevel.Warning,
        Message = "LiveKit webhook: tanani o'qib bo'lmadi (uzunlik={Length}).")]
    internal static partial void WebhookMalformed(ILogger logger, int length);

    [LoggerMessage(
        EventId = 6501,
        Level = LogLevel.Debug,
        Message = "LiveKit webhook: takroriy hodisa e'tiborsiz qoldirildi. "
                  + "event={EventName} id={EventId}")]
    internal static partial void WebhookDuplicate(ILogger logger, string eventName, string eventId);

    [LoggerMessage(
        EventId = 6502,
        Level = LogLevel.Warning,
        Message = "LiveKit webhook: noma'lum egress. event={EventName} egress={EgressId}")]
    internal static partial void WebhookUnknownEgress(ILogger logger, string eventName, string egressId);

    [LoggerMessage(
        EventId = 6503,
        Level = LogLevel.Information,
        Message = "Dars yozuvi yangilandi: event={EventName} egress={EgressId} "
                  + "yozuv={RecordingId} holat={Status}")]
    internal static partial void WebhookApplied(
        ILogger logger, string eventName, string egressId, long recordingId, string status);

    // ================================================================= boshlash/to'xtatish

    [LoggerMessage(
        EventId = 6510,
        Level = LogLevel.Information,
        Message = "Dars yozuvi boshlandi: yozuv={RecordingId} dars={SessionId} egress={EgressId}")]
    internal static partial void Started(
        ILogger logger, long recordingId, long sessionId, string egressId);

    [LoggerMessage(
        EventId = 6511,
        Level = LogLevel.Error,
        Message = "Dars yozuvini boshlab bo'lmadi: yozuv={RecordingId} dars={SessionId} "
                  + "urinish={Attempts} sabab={Reason}")]
    internal static partial void StartFailed(
        ILogger logger, long recordingId, long sessionId, int attempts, string reason);

    [LoggerMessage(
        EventId = 6512,
        Level = LogLevel.Information,
        Message = "Dars yozuvini to'xtatish so'raldi: yozuv={RecordingId} egress={EgressId} "
                  + "qabul={Accepted}")]
    internal static partial void StopRequested(
        ILogger logger, long recordingId, string egressId, bool accepted);

    // ================================================================= avtomatik yozuv

    [LoggerMessage(
        EventId = 6520,
        Level = LogLevel.Information,
        Message = "Avtomatik yozuv navbatga qo'yildi (guruh sozlamasi). dars={SessionId}")]
    internal static partial void AutoQueued(ILogger logger, long sessionId);

    /// <summary>
    /// ★ NIMA UCHUN <c>Warning</c>, <c>Debug</c> EMAS: guruhda yozuv
    /// YOQILGAN, lekin dars YOZILMAYDI — ya'ni o'quv bo'limi kutgan narsa
    /// bo'lmaydi va buni faqat dars tugagach, yozuv yo'qligidan bilishardi.
    /// Bu logdagi yagona ogohlantirish shu holatga oid.
    /// </summary>
    [LoggerMessage(
        EventId = 6521,
        Level = LogLevel.Warning,
        Message = "Avtomatik yozuv o'tkazib yuborildi: LiveKit yoki ombor sozlanmagan. "
                  + "dars={SessionId}")]
    internal static partial void AutoSkippedNotConfigured(ILogger logger, long sessionId);

    // ================================================================= trek quvuri (SPEC-RECORDING-V2)
    //
    // ★ NIMA UCHUN AYNI SINFDA, ALOHIDA `TrackRecordingLog` EMAS: sinf
    //   izohidagi sabab — modulning BARCHA xabarlari va EventId'lari bir
    //   joyda tursin. Ikkinchi sinf ikkinchi EventId makonini boshqarishni
    //   talab qilardi va ikkalasi bir kun to'qnashardi.

    [LoggerMessage(
        EventId = 6530,
        Level = LogLevel.Information,
        Message = "Dars bo'lagi yangilandi: event={EventName} egress={EgressId} "
                  + "bo'lak={TrackId} holat={Status}")]
    internal static partial void TrackWebhookApplied(
        ILogger logger, string eventName, string egressId, long trackId, string status);

    [LoggerMessage(
        EventId = 6531,
        Level = LogLevel.Information,
        Message = "Dars bo'lagi yozila boshladi: bo'lak={TrackId} yozuv={RecordingId} "
                  + "tur={Kind} egress={EgressId}")]
    internal static partial void TrackStarted(
        ILogger logger, long trackId, long recordingId, string kind, string egressId);

    /// <summary>
    /// ⚠️ `Error`, lekin YAKUNIY xato EMAS: qator `Requested` da qoladi va
    /// tiklash vazifasi qayta uradi. Daraja baribir `Error` —
    /// `RecordingLog.StartFailed` bilan AYNI mulohaza: LiveKit so'rovni
    /// rad etayotgan bo'lsa, buni dars tugagandan keyin emas, O'SHA ZAHOTI
    /// bilish kerak.
    /// </summary>
    [LoggerMessage(
        EventId = 6532,
        Level = LogLevel.Error,
        Message = "Dars bo'lagini boshlab bo'lmadi: bo'lak={TrackId} yozuv={RecordingId} "
                  + "tur={Kind} urinish={Attempts} sabab={Reason}")]
    internal static partial void TrackStartFailed(
        ILogger logger, long trackId, long recordingId, string kind, int attempts, string reason);

    [LoggerMessage(
        EventId = 6533,
        Level = LogLevel.Information,
        Message = "Dars bo'lagini to'xtatish so'raldi: bo'lak={TrackId} yozuv={RecordingId} "
                  + "egress={EgressId}")]
    internal static partial void TrackStopRequested(
        ILogger logger, long trackId, long recordingId, string egressId);

    /// <summary>
    /// ⚠️ `Warning`, `Error` EMAS — VA BU SPEC (§3.3) NING OSHKOR TALABI.
    /// LiveKit allaqachon o'zi to'xtatgan egress uchun rad javobini
    /// qaytaradi va bu darsning ODATIY yakuni: xona yopilganda egress
    /// o'zi to'xtaydi, biz esa kechikib so'rov yuboramiz.
    /// </summary>
    [LoggerMessage(
        EventId = 6534,
        Level = LogLevel.Warning,
        Message = "Dars bo'lagini to'xtatish rad etildi (odatda allaqachon to'xtagan): "
                  + "bo'lak={TrackId} yozuv={RecordingId} egress={EgressId}")]
    internal static partial void TrackStopRefused(
        ILogger logger, long trackId, long recordingId, string egressId);

    /// <summary>
    /// 🔴 BU XABAR PRODUKSIYA DALILI UCHUN: xom faylning kengaytmasi
    /// `mime_type` dan BASHORAT qilinadi (SPEC §2.8 dagi jadval) va
    /// bashorat noto'g'ri bo'lsa buni boshqa hech narsa ko'rsatmaydi —
    /// kalit jimgina to'g'rilanadi va jadval xato bo'lib qolaverardi.
    /// Birinchi darslardan keyin shu qatorlar bo'yicha jadvalni tuzatish
    /// kerak.
    /// </summary>
    [LoggerMessage(
        EventId = 6535,
        Level = LogLevel.Warning,
        Message = "Xom bo'lak kaliti bashoratdan farq qildi: bo'lak={TrackId} "
                  + "bashorat={Predicted} haqiqiy={Actual}")]
    internal static partial void TrackObjectKeyDiffers(
        ILogger logger, long trackId, string predicted, string actual);

    // ================================================================= watchdog

    [LoggerMessage(
        EventId = 6550,
        Level = LogLevel.Warning,
        Message = "Watchdog: yozuv boshlanmagan, qayta urinamiz. "
                  + "yozuv={RecordingId} urinish={Attempts}")]
    internal static partial void WatchdogRetry(ILogger logger, long recordingId, int attempts);

    [LoggerMessage(
        EventId = 6551,
        Level = LogLevel.Error,
        Message = "Watchdog: yozuv yakuniy XATO deb belgilandi. "
                  + "yozuv={RecordingId} sabab={Reason}")]
    internal static partial void WatchdogGaveUp(ILogger logger, long recordingId, string reason);

    [LoggerMessage(
        EventId = 6552,
        Level = LogLevel.Information,
        Message = "Watchdog: fayl ombordan topildi, yozuv yakunlandi. "
                  + "yozuv={RecordingId} kalit={ObjectKey} hajm={SizeBytes}")]
    internal static partial void WatchdogRecovered(
        ILogger logger, long recordingId, string objectKey, long? sizeBytes);

    [LoggerMessage(
        EventId = 6553,
        Level = LogLevel.Warning,
        Message = "Watchdog: ombor javob bermadi, yozuv keyingi yurishga qoldirildi. "
                  + "yozuv={RecordingId}")]
    internal static partial void WatchdogStorageUnavailable(
        ILogger logger, Exception exception, long recordingId);

    /// <summary>
    /// ★ `Debug` — bu NOSOZLIK EMAS, ODATIY KUTISH. Dars boshlangandan
    /// keyin xona bir necha soniya bo'sh turishi normal holat. `Warning`
    /// bo'lsa har bir dars boshida log axlat bilan to'lardi va haqiqiy
    /// ogohlantirishlar ko'rinmay qolardi.
    /// </summary>
    [LoggerMessage(
        EventId = 6554,
        Level = LogLevel.Debug,
        Message = "Watchdog: xona hali bo'sh, yozuv kutilmoqda. yozuv={RecordingId}")]
    internal static partial void WatchdogWaitingForParticipants(
        ILogger logger, long recordingId);

    /// <summary>
    /// ⚠️ Presence o'qilmadi — yozuv BARIBIR boshlanadi (fail-open).
    /// Sabab: `RecordingWatchdogJob.RoomHasParticipantsAsync`.
    /// </summary>
    [LoggerMessage(
        EventId = 6555,
        Level = LogLevel.Warning,
        Message = "Watchdog: ishtirokchilar ro'yxati o'qilmadi, yozuv baribir "
                  + "boshlanadi. yozuv={RecordingId}")]
    internal static partial void WatchdogPresenceUnavailable(
        ILogger logger, Exception exception, long recordingId);

    // ================================================================= trek moslashtiruvchisi (§4.1)
    //
    // ★ Bu vazifa YO'QOLGAN webhook'larning zaxira yo'li. Uning har bir
    //   xabari "biror hodisa yetib kelmagan" degani, ya'ni ularning
    //   ko'payishi webhook kanalida muammo borligini bildiradi. Shuning
    //   uchun daraja `Debug` emas.

    [LoggerMessage(
        EventId = 6536,
        Level = LogLevel.Warning,
        Message = "Moslashtiruvchi: bo'lak boshlanmagan, qayta urinamiz. "
                  + "bo'lak={TrackId} yozuv={RecordingId} urinish={Attempts}")]
    internal static partial void ReconcileTrackRetry(
        ILogger logger, long trackId, long recordingId, int attempts);

    [LoggerMessage(
        EventId = 6537,
        Level = LogLevel.Error,
        Message = "Moslashtiruvchi: bo'lak YAKUNIY xato deb belgilandi. "
                  + "bo'lak={TrackId} yozuv={RecordingId} sabab={Reason}")]
    internal static partial void ReconcileTrackGaveUp(
        ILogger logger, long trackId, long recordingId, string reason);

    [LoggerMessage(
        EventId = 6538,
        Level = LogLevel.Information,
        Message = "Moslashtiruvchi: bo'lak fayli ombordan topildi. "
                  + "bo'lak={TrackId} yozuv={RecordingId} hajm={SizeBytes}")]
    internal static partial void ReconcileTrackRecovered(
        ILogger logger, long trackId, long recordingId, long? sizeBytes);

    /// <summary>
    /// 🔴 Bu vazifadagi ENG QIMMATLI qadam: yo'qolgan video bo'lak bir
    /// necha daqiqalik tasvirni yo'qotadi, yo'qolgan mikser esa BUTUN
    /// DARSNING OVOZINI.
    /// </summary>
    [LoggerMessage(
        EventId = 6539,
        Level = LogLevel.Warning,
        Message = "Moslashtiruvchi: xona ovozi qatori yo'q edi, mikser yoqildi. "
                  + "yozuv={RecordingId} sentinel={TrackSid}")]
    internal static partial void ReconcileMixerEnsured(
        ILogger logger, long recordingId, string trackSid);

    [LoggerMessage(
        EventId = 6540,
        Level = LogLevel.Error,
        Message = "Moslashtiruvchi: xona ovozi mikseri LiveKit'da topilmadi — o'rniga "
                  + "yangisi qo'yiladi. yozuv={RecordingId} bo'lak={TrackId} egress={EgressId}")]
    internal static partial void ReconcileMixerDead(
        ILogger logger, long recordingId, long trackId, string egressId);

    [LoggerMessage(
        EventId = 6541,
        Level = LogLevel.Warning,
        Message = "Moslashtiruvchi: LiveKit'da bizga noma'lum trek topildi (webhook "
                  + "yo'qolgan). yozuv={RecordingId} trek={TrackSid} tur={Kind}")]
    internal static partial void ReconcileTrackDiscovered(
        ILogger logger, long recordingId, string trackSid, string kind);

    [LoggerMessage(
        EventId = 6542,
        Level = LogLevel.Warning,
        Message = "Moslashtiruvchi: xona ovozi juda uzoq yozilmoqda, to'xtatildi. "
                  + "yozuv={RecordingId} bo'lak={TrackId}")]
    internal static partial void ReconcileMixerOverlong(
        ILogger logger, long recordingId, long trackId);

    [LoggerMessage(
        EventId = 6543,
        Level = LogLevel.Information,
        Message = "Moslashtiruvchi: yozuv tungi navbatga tushdi. "
                  + "yozuv={RecordingId} tayyor_bo'lak={Completed}")]
    internal static partial void ReconcileQueued(
        ILogger logger, long recordingId, int completed);

    [LoggerMessage(
        EventId = 6544,
        Level = LogLevel.Error,
        Message = "Moslashtiruvchi: darsdan bironta ham bo'lak chiqmadi. yozuv={RecordingId}")]
    internal static partial void ReconcileNoTracks(ILogger logger, long recordingId);

    /// <summary>
    /// 🔴 LIVEKIT JAVOB BERMASA HECH QANDAY XULOSA CHIQARILMAYDI. Bo'sh
    /// ro'yxatni "mikser o'lgan" deb tushunish tirik mikser ustiga
    /// ikkinchisini yoqardi — bitta darsda ikki ovoz fayli va montajda
    /// har ovoz ikki marta.
    /// </summary>
    [LoggerMessage(
        EventId = 6545,
        Level = LogLevel.Warning,
        Message = "Moslashtiruvchi: LiveKit holatini o'qib bo'lmadi, xulosa chiqarilmadi. "
                  + "yozuv={RecordingId} sabab={Reason}")]
    internal static partial void ReconcileLiveKitUnavailable(
        ILogger logger, long recordingId, string reason);

    [LoggerMessage(
        EventId = 6546,
        Level = LogLevel.Warning,
        Message = "Moslashtiruvchi: ombor javob bermadi, bo'lak keyingi yurishga qoldirildi. "
                  + "bo'lak={TrackId}")]
    internal static partial void ReconcileStorageUnavailable(
        ILogger logger, Exception exception, long trackId);

    // ================================================================= tungi yig'ish (§4.3–4.5)

    [LoggerMessage(
        EventId = 6560,
        Level = LogLevel.Information,
        Message = "Tungi yig'ish boshlandi: yozuv={RecordingId} uzilgan_ishdan={TookOver}")]
    internal static partial void CompositionClaimed(
        ILogger logger, long recordingId, bool tookOver);

    [LoggerMessage(
        EventId = 6561,
        Level = LogLevel.Information,
        Message = "Tungi yig'ish yakunlandi: yozuv={RecordingId} hajm={SizeBytes} "
                  + "uzunlik={DurationSeconds}s")]
    internal static partial void CompositionCompleted(
        ILogger logger, long recordingId, long sizeBytes, int durationSeconds);

    [LoggerMessage(
        EventId = 6562,
        Level = LogLevel.Warning,
        Message = "Tungi yig'ish yiqildi, navbatga qaytarildi: yozuv={RecordingId} "
                  + "urinish={Attempts} sabab={Reason}")]
    internal static partial void CompositionRetrying(
        ILogger logger, long recordingId, int attempts, string reason);

    [LoggerMessage(
        EventId = 6563,
        Level = LogLevel.Error,
        Message = "Tungi yig'ish YAKUNIY xato: yozuv={RecordingId} sabab={Reason}")]
    internal static partial void CompositionFailed(
        ILogger logger, long recordingId, string reason);

    /// <summary>
    /// ★ `Information` — bu NOSOZLIK EMAS. Uzilish "navbat kechadan uzun
    /// bo'ldi" degani va u kutilgan hol. Yakuniy taslim bo'lish esa
    /// alohida `CompositionFailed` qatori bilan yoziladi.
    /// </summary>
    [LoggerMessage(
        EventId = 6564,
        Level = LogLevel.Information,
        Message = "Tungi oyna tugadi, yig'ish keyingi kechaga qoldirildi: "
                  + "yozuv={RecordingId} uzilish={Interruptions} yakuniy={Final}")]
    internal static partial void CompositionInterrupted(
        ILogger logger, long recordingId, int interruptions, bool final);

    /// <summary>
    /// 🔴 §9.1 NING YAGONA AVTOMATIK O'LCHOVI. Xona ovozi qatoridagi farq
    /// ayniqsa muhim: u vaqt o'qining O'ZI, ya'ni uning siljishi butun
    /// yozuvning siljishi. O'n darsdan keyin bu qatorlar bo'yicha siljish
    /// TEZLIGI hisoblanadi.
    /// </summary>
    [LoggerMessage(
        EventId = 6565,
        Level = LogLevel.Warning,
        Message = "Xom bo'lakning uzunligi kutilganidan farq qildi: yozuv={RecordingId} "
                  + "bo'lak={TrackId} tur={Kind} kutilgan={ExpectedMs}ms o'lchangan={ProbedMs}ms")]
    internal static partial void CompositionDrift(
        ILogger logger, long recordingId, long trackId, string kind, int expectedMs, int probedMs);

    /// <summary>
    /// 🔴 IKKI KODLOVCHI BITTA KALITGA YOZAYOTGANINING YAGONA ALOMATI.
    /// Bu qator chiqsa — ijara sozlamalari (muddat va uzaytirish oralig'i)
    /// qayta ko'rib chiqilishi kerak.
    /// </summary>
    [LoggerMessage(
        EventId = 6566,
        Level = LogLevel.Error,
        Message = "Yig'ish ijarasi yo'qoldi — qatorni boshqa ishchi olgan bo'lishi mumkin. "
                  + "yozuv={RecordingId}")]
    internal static partial void CompositionLeaseLost(ILogger logger, long recordingId);

    [LoggerMessage(
        EventId = 6567,
        Level = LogLevel.Warning,
        Message = "Yig'ish ijarasini uzaytirib bo'lmadi. yozuv={RecordingId}")]
    internal static partial void CompositionLeaseRenewFailed(
        ILogger logger, Exception exception, long recordingId);

    [LoggerMessage(
        EventId = 6568,
        Level = LogLevel.Error,
        Message = "Tungi yig'ishda kutilmagan xato. yozuv={RecordingId}")]
    internal static partial void CompositionCrashed(
        ILogger logger, Exception exception, long recordingId);

    [LoggerMessage(
        EventId = 6569,
        Level = LogLevel.Warning,
        Message = "Oldingi urinishdan qolgan yakuniy faylni o'chirib bo'lmadi. "
                  + "yozuv={RecordingId} kalit={ObjectKey}")]
    internal static partial void CompositionLeftoverNotRemoved(
        ILogger logger, Exception exception, long recordingId, string objectKey);

    [LoggerMessage(
        EventId = 6570,
        Level = LogLevel.Information,
        Message = "Xom bo'laklar ombordan o'chirildi: yozuv={RecordingId} soni={Count}")]
    internal static partial void RawPurged(ILogger logger, long recordingId, int count);

    /// <summary>
    /// ⚠️ `Warning`, `Error` EMAS — VA BU SPEC (§4.5-9) NING OSHKOR
    /// TALABI: o'chirishning yiqilishi yig'ishning nosozligi EMAS.
    /// `RawPurgedAt` bo'sh qoladi va keyingi kecha qayta uriniladi.
    /// Yetim xom fayl PUL turadi, orqaga qaytarilgan sog'lom yozuv esa
    /// BUTUN DARSNI.
    /// </summary>
    [LoggerMessage(
        EventId = 6571,
        Level = LogLevel.Warning,
        Message = "Xom bo'lakni o'chirib bo'lmadi, keyingi kecha qayta uriniladi. "
                  + "yozuv={RecordingId} kalit={ObjectKey}")]
    internal static partial void RawPurgeFailed(
        ILogger logger, Exception exception, long recordingId, string objectKey);
}
