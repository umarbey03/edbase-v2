namespace Zinnur.Domain.Enums;

/// <summary>
/// Xom bo'lakning TURI — tungi yig'ishda u qayerga qo'yilishini
/// belgilaydi.
///
/// ★ NIMA UCHUN "video/ovoz" degan ikki qiymat YETMAYDI: yig'ish
/// jarayonida uchala savol ham kerak bo'ladi — bo'lak KANVASning qayeriga
/// tushadi (kamera kichik oynacha, ekran to'liq fon), u LiveKit TREKimi
/// yoki BUTUN XONA aralashmasimi, va uni umuman kutish kerakmi. Turlarni
/// birlashtirsak, bu farqlar servis qatlamiga <c>if</c> bo'lib ko'chardi.
///
/// ⚠️ RAQAMLAR BAZAGA YOZILADI. Yangi tur FAQAT oxiriga qo'shiladi.
///
/// 🔴 LiveKit'ning boshqa manbalari (<c>UNKNOWN</c> va kelajakdagi
/// qiymatlar) E'TIBORSIZ QOLDIRILADI — ular uchun qator YARATILMAYDI.
/// Noma'lum manbani "video" deb taxmin qilish yig'ishda tushunarsiz
/// natija berardi, xatoni esa faqat ertalab, tayyor faylni ochganda
/// bilardik.
///
/// ── OVOZ: IKKI REJIM, ULAR BIR VAQTDA BO'LMAYDI ──────────────────────
///
/// Qaysi ovoz turlari paydo bo'lishini <c>recordings.audio_capture_mode</c>
/// sozlamasi hal qiladi:
///
///   • <c>RoomComposite</c> (standart) — faqat bitta <see cref="RoomAudio"/>.
///     O'quvchilarning ovozi ham shu aralashmada.
///   • <c>TeacherTrack</c> (zaxira yo'l) — <see cref="MicAudio"/> va
///     <see cref="ScreenAudio"/>, o'quvchilarsiz.
///
/// 🔴 IKKALASI BIR VAQTDA HECH QACHON BO'LMAYDI. Xona aralashmasida
/// ustozning ovozi ALLAQACHON bor; ustiga alohida mikrofon faylini
/// qo'shsak, bir ovoz ikki marta, ozgina siljish bilan yangrardi. Bu
/// aks-sado emas, "comb filter" — buzilgan mikrofonga o'xshaydi va uni
/// tez topib bo'lmaydi.
/// </summary>
public enum RecordingTrackKind
{
    /// <summary>
    /// Ustozning KAMERASI (LiveKit <c>TrackSource.CAMERA</c>) —
    /// <c>TrackEgress</c>, qayta kodlashsiz.
    /// </summary>
    CameraVideo = 0,

    /// <summary>
    /// EKRAN ULASHISH tasviri (LiveKit <c>TrackSource.SCREEN_SHARE</c>) —
    /// <c>TrackEgress</c>, qayta kodlashsiz.
    ///
    /// Bir darsda bir NECHTA bo'lishi ODATIY hol: ekran har yoqilib
    /// o'chirilganda yangi trek va yangi fayl paydo bo'ladi.
    /// </summary>
    ScreenVideo = 1,

    /// <summary>
    /// Ustozning MIKROFONI (LiveKit <c>TrackSource.MICROPHONE</c>).
    ///
    /// ⚠️ FAQAT <c>TeacherTrack</c> zaxira rejimida yaratiladi. Standart
    /// rejimda bu trek e'tiborsiz qoldiriladi — uning ovozi
    /// <see cref="RoomAudio"/> aralashmasida allaqachon bor.
    /// </summary>
    MicAudio = 2,

    /// <summary>
    /// Ekran ulashish bilan kelgan OVOZ (LiveKit
    /// <c>TrackSource.SCREEN_SHARE_AUDIO</c>).
    ///
    /// ⚠️ <see cref="MicAudio"/> bilan AYNI shart: faqat <c>TeacherTrack</c>
    /// rejimida.
    /// </summary>
    ScreenAudio = 3,

    /// <summary>
    /// BUTUN XONANING aralashtirilgan ovozi — faqat-ovozli
    /// <c>RoomCompositeEgress</c> chiqargan bitta uzluksiz fayl: ustoz,
    /// ekran ovozi va gapirgan HAR BIR o'quvchi.
    ///
    /// 🔴 BU LIVEKIT TREKI EMAS va shu farq amaliy oqibatlarga ega:
    ///
    ///   • uning <c>TrackSid</c> i yo'q — o'rniga
    ///     <c>RecordingTrack.RoomAudioSid</c> sentineli yoziladi;
    ///   • u hech kimga tegishli emas — <c>ParticipantIdentity</c> bo'sh
    ///     (<c>NULL</c>), chunki aralashma bitta ishtirokchining mulki
    ///     emas;
    ///   • <c>track_unpublished</c> hodisasi unga HECH QACHON tegmaydi.
    ///
    /// ★ U — VAQT O'QI. Fayl dars boshidan oxirigacha uzluksiz yoziladi
    /// (ovoz o'chirilsa ham yozuv to'xtamaydi, shunchaki jimlik tushadi),
    /// shuning uchun video bo'laklar AYNAN shu o'qqa nisbatan
    /// joylashtiriladi. Bitta uzluksiz o'q — bu "drift yig'ilishi" degan
    /// butun bir xatolar sinfini yo'q qiladi.
    /// </summary>
    RoomAudio = 4,
}
