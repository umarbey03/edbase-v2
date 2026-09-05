using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// <see cref="IAutoRecordingScheduler"/> ning amalga oshirilishi.
///
/// ── TO'RTTA DARVOZA, QAT'IY SHU TARTIBDA ────────────────────────────────
///
///  1) GURUH KALITI (<c>Group.RecordEnabled</c>) — eng arzon va eng
///     tez-tez rad etadigan shart, shuning uchun BIRINCHI. Yozuvi o'chiq
///     guruhning darsi boshlanganda bu metod bitta ham so'rov yubormaydi.
///  2) DARS JONLIMI — <see cref="IRecordingService.StartAsync"/> dagi AYNI
///     shart. Bu yerda u himoya sifatida: chaqiruvchi <c>Start()</c> dan
///     KEYIN chaqirishi shart va shart buzilsa jimgina o'tib ketmasin.
///  3) XIZMAT SOZLANGANMI — sozlanmagan bo'lsa qator UMUMAN qo'shilmaydi.
///     🔴 SABAB: watchdog <c>!egress.IsConfigured</c> holatida HECH NARSA
///     qilmaydi, ya'ni qator navbatda abadiy yotardi; sozlama tuzatilganda
///     esa dars allaqachon tugagan bo'lib, watchdog uni "Dars yakunlandi,
///     yozuv esa boshlanmadi" deb `Failed` qilardi. Natija — har dars
///     uchun bittadan yolg'on xato qatori va "yozuvlar buzuq" degan
///     taassurot. Sozlanmagan xizmatda TO'G'RI xulq — hech narsa
///     va'da qilmaslik.
///  4) IDEMPOTENTLIK — pastdagi izohga qarang.
///
/// ── NIMA UCHUN IDEMPOTENTLIK TEKSHIRUVI KERAK ───────────────────────────
///
/// ⚠️ <c>LiveSession.Start()</c> darsni <c>Live</c> dan <c>Live</c> ga
/// o'tkazishni RAD ETMAYDI (u faqat <c>Ended</c>/<c>Cancelled</c> ni
/// to'sadi va <c>ActualStart</c> ni bir marta yozadi). Ya'ni "Darsni
/// boshlash" ikkinchi qurilmadan yoki <c>curl</c> bilan qayta
/// chaqirilishi mumkin. Tekshiruvsiz har chaqiruv yangi navbat qatori
/// yasab, watchdog ularning HAR BIRI uchun alohida egress ochardi — bir
/// darsning bir necha nusxasi, ikki barobar tarmoq va ombor. Bu
/// <see cref="IRecordingService.StartAsync"/> dagi AYNI qoida va u yerda
/// ham AYNI sababdan turadi.
///
/// So'rov <c>IX_SessionRecordings_SessionId_Id</c> indeksiga tushadi.
///
/// ★ 2026-09-05 (SPEC-RECORDING-V2 §5.9-2): TEKSHIRUV QUVUR BO'YICHA
/// BO'LDI. Ilgari "shu dars uchun YAKUNLANMAGAN qator bormi?" so'ralardi;
/// endi "shu dars uchun AYNI QUVURDA yakunlanmagan qator bormi?". Sabab:
/// solishtiruv rejimida bitta dars IKKITA qator bilan yoziladi (eski +
/// yangi usul) va eski shart ikkinchisini har doim to'sib qo'yardi.
///
/// ── QAYSI QATORLAR YARATILADI — TO'LIQ HAQIQAT JADVALI (§2.7) ───────────
///
/// | RecordEnabled | umumiy kalit | ro'yxatda | guruh ustuni     | qatorlar       |
/// |---------------|--------------|-----------|------------------|----------------|
/// | false         | —            | —         | —                | yo'q           |
/// | true          | false        | —         | —                | 1 eski         |
/// | true          | true         | HA        | ahamiyatsiz      | 2 (eski+yangi) |
/// | true          | true         | yo'q      | RoomComposite    | 1 eski         |
/// | true          | true         | yo'q      | TrackComposition | 1 yangi        |
///
/// 🔴 SOLISHTIRUV RO'YXATI GURUH USTUNIDAN USTUN va IKKI QATOR OLISHNING
/// YAGONA yo'li. Yoyish rejasining 3-bosqichi aynan shunga tayanadi:
/// bitta guruh (ATF-97) ikkala faylni ham beradi va o'quv bo'limi ularni
/// yonma-yon solishtiradi. Ro'yxat bo'shatilgach ortiqcha qator ham
/// yo'qoladi — ya'ni bu holat bazada iz qoldirmaydi.
///
/// 🔴 SOZLAMA O'QILMASA — ESKI USUL. Kalitlar registrda bo'lmasa yoki
/// qiymat buzuq bo'lsa yangi quvur YOQILMAYDI. "Standart — bugungi
/// xatti-harakat" qoidasi bu faylda hech qachon buzilmasin: noaniqlik
/// paytida hech bo'lmaganda ISHLAYDIGAN yo'l tanlanadi.
/// </summary>
public sealed class AutoRecordingScheduler(
    IApplicationDbContext db,
    ILiveKitEgress egress,
    IRecordingStorage storage,
    ISettingsResolver settings,
    TimeProvider clock,
    ILogger<AutoRecordingScheduler> logger) : IAutoRecordingScheduler
{
    /// <inheritdoc />
    public async Task<bool> EnqueueAsync(LiveSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        // ── 1. Guruh kaliti va guruhning yozuv usuli ───────────────────
        //
        // ★ `Group` odatda ALLAQACHON yuklangan (`LoadAndAuthorizeAsync`
        //   uni `Include` qiladi) — ya'ni odatiy yo'lda qo'shimcha so'rov
        //   YO'Q.
        //
        // 🔴 ZAXIRA SO'ROV ATAYLAB BOR. `Group` yuklanmagan bo'lsa
        //   `?.RecordEnabled` `false` berardi va butun avtomatik yozuv
        //   JIMGINA o'chib qolardi — bir kun kimdir `Include` ni olib
        //   tashlaganda buni hech qanday test va hech qanday log
        //   ko'rsatmasdi. "Ma'lumot yo'q" bilan "yozuv o'chiq" ni
        //   aralashtirmaslik uchun bu holatda bazadan SO'RAYMIZ.
        //
        // ★ IKKALA USTUN BITTA SO'ROVDA: ular BIR QARORNING ikki qismi
        //   ("yozilsinmi" va "qanday yozilsin"), ya'ni ikkinchisi uchun
        //   ikkinchi marta bazaga borish bekorga bo'lardi.
        bool recordEnabled;
        RecordingPipeline groupPipeline;

        if (session.Group is { } group)
        {
            recordEnabled = group.RecordEnabled;
            groupPipeline = group.RecordingPipeline;
        }
        else
        {
            var row = await db.Groups
                .AsNoTracking()
                .Where(g => g.Id == session.GroupId)
                .Select(g => new { g.RecordEnabled, g.RecordingPipeline })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            recordEnabled = row?.RecordEnabled ?? false;
            groupPipeline = row?.RecordingPipeline ?? RecordingPipeline.RoomComposite;
        }

        if (!recordEnabled)
            return false;

        // ── 2. Dars jonlimi ────────────────────────────────────────────
        if (session.Status != SessionStatus.Live)
            return false;

        // ── 3. Xizmat sozlanganmi ──────────────────────────────────────
        //
        // Bu TASHQI CHAQIRUV EMAS: `IsConfigured` ish paytidagi sozlama
        // kesimini o'qiydi (`IRuntimeOptions.Current`), ya'ni tarmoqqa
        // chiqmaydi va dars boshlashni sekinlashtirmaydi.
        if (!egress.IsConfigured)
        {
            RecordingLog.AutoSkippedNotConfigured(logger, session.Id);

            return false;
        }

        // ── 4. Qaysi quvur(lar) ────────────────────────────────────────
        var (useRoomComposite, useTrackComposition, viaShadowList) =
            await SelectPipelinesAsync(session.GroupId, groupPipeline, ct).ConfigureAwait(false);

        // ── 5. Idempotentlik — QUVUR BO'YICHA ──────────────────────────
        //
        // Bitta so'rov, ko'pi bilan ikkita qator: `SessionId` + quvur
        // juftligi bazada UNIKAL (`UX_SessionRecordings_SessionId_Pipeline_Active`,
        // filtr `"Status" < 3`), ya'ni bu ro'yxat hech qachon uzun bo'lmaydi.
        var busy = await db.SessionRecordings
            .AsNoTracking()
            .Where(r => r.SessionId == session.Id
                     && r.Status != RecordingStatus.Completed
                     && r.Status != RecordingStatus.Failed)
            .Select(r => r.Pipeline)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var queued = false;

        if (useRoomComposite && !busy.Contains(RecordingPipeline.RoomComposite))
        {
            Add(session, RecordingPipeline.RoomComposite);
            RecordingLog.AutoQueued(logger, session.Id);
            queued = true;
        }

        if (useTrackComposition && !busy.Contains(RecordingPipeline.TrackComposition))
        {
            Add(session, RecordingPipeline.TrackComposition);
            RecordingLog.AutoQueuedTrackPipeline(logger, session.Id, viaShadowList);
            queued = true;
        }

        return queued;
    }

    /// <summary>
    /// Bitta navbat qatorini qo'shadi (saqlamaydi).
    ///
    /// ★ `RequestedBy = null` — "TIZIM BOSHLADI". Maydon `SessionRecording`
    ///   da allaqachon `nullable` va aynan shu ma'no uchun hujjatlangan,
    ///   ya'ni migratsiya KERAK EMAS. Qo'lda boshlangan yozuvda esa u
    ///   hamon xodimning Id'sini saqlardi — "kim yozib olishga qaror
    ///   qildi" savoli javobsiz qolmaydi, javob shunchaki ikki xil
    ///   bo'ladi: "falonchi xodim" yoki "guruh sozlamasi".
    ///
    /// ★ IKKALA QUVUR HAM AYNI KALIT SXEMASINI ISHLATADI
    ///   (<c>BuildObjectKey</c>). Kalitning 8 tasodifiy bayti ikki qator
    ///   bir faylga yozishini IMKONSIZ qiladi, ya'ni solishtiruv rejimida
    ///   ikki fayl bir-birini ustiga yozib yubora olmaydi. Yangi quvurda
    ///   bu kalitga TUNGI MONTAJ yozadi, dars davomida esa u bo'sh
    ///   turadi — xom bo'laklar butunlay boshqa prefiksda (`raw/…`).
    /// </summary>
    private void Add(LiveSession session, RecordingPipeline pipeline)
    {
        var recording = new SessionRecording
        {
            SessionId = session.Id,
            RequestedBy = null,
            ObjectKey = storage.BuildObjectKey(session.Id),
            Pipeline = pipeline,
        };

        // Yangi quvurda qator "yig'ilmoqda" holatida tug'iladi: dars
        // ketayotganda xom bo'laklar to'planadi, montaj esa kechasi.
        // Eski quvurda bu bosqich UMUMAN yo'q va metod istisno tashlaydi,
        // shuning uchun shart oshkora.
        if (pipeline == RecordingPipeline.TrackComposition)
            recording.BeginComposition(clock.GetUtcNow());

        // ⚠️ `SaveChanges` YO'Q — chaqiruvchining tranzaksiyasi (izoh:
        //    `IAutoRecordingScheduler`).
        db.SessionRecordings.Add(recording);
    }

    /// <summary>
    /// §2.7 haqiqat jadvalining ijrosi: qaysi quvur(lar) shu dars uchun
    /// qator yaratadi.
    /// </summary>
    /// <returns>
    /// <c>ViaShadowList</c> — yangi quvur guruh USTUNIDAN emas, solishtiruv
    /// RO'YXATIDAN kelganini bildiradi. U faqat log uchun, lekin 3-bosqichda
    /// muhim: "nega bu darsda ikkita yozuv bor?" savoliga log darhol javob
    /// beradi.
    /// </returns>
    private async Task<(bool Room, bool Track, bool ViaShadowList)> SelectPipelinesAsync(
        long groupId, RecordingPipeline groupPipeline, CancellationToken ct)
    {
        // 🔴 FAVQULODDA TORMOZ BIRINCHI: o'chiq bo'lsa guruh ustuni ham,
        //    solishtiruv ro'yxati ham O'QILMAYDI. Bu — deploysiz orqaga
        //    qaytish yo'li va u shartsiz ishlashi kerak.
        var toggles = await ReadTogglesAsync(ct).ConfigureAwait(false);

        if (!toggles.Enabled)
            return (Room: true, Track: false, ViaShadowList: false);

        var (inShadowList, invalid) = ParseShadowGroups(toggles.ShadowGroups, groupId);

        if (invalid is { Length: > 0 })
            RecordingLog.AutoShadowListMalformed(logger, invalid);

        if (inShadowList)
            return (Room: true, Track: true, ViaShadowList: true);

        return groupPipeline == RecordingPipeline.TrackComposition
            ? (Room: false, Track: true, ViaShadowList: false)
            : (Room: true, Track: false, ViaShadowList: false);
    }

    /// <summary>
    /// Ikkala kalitni BITTA baza so'rovi bilan o'qiydi
    /// (<see cref="ISettingsResolver.ResolveManyAsync"/>).
    ///
    /// ★ NIMA UCHUN BU YERDA SO'ROV BOR, LEKIN U ZARARSIZ: metod dars
    ///   boshlash yo'lida turadi, shuning uchun so'rov FAQAT yozuvi
    ///   yoqilgan guruh uchun va faqat bir marta ketadi (yuqoridagi uchta
    ///   darvoza allaqachon o'tgan). Ikkita alohida `ResolveAsync` esa
    ///   ikkita so'rov bo'lardi.
    ///
    /// ⚠️ KALIT REGISTRDA YO'Q BO'LSA — YANGI QUVUR O'CHIQ. Amalda bu
    ///   bo'lmaydi (registr ham, bu fayl ham bitta assembly'da), lekin
    ///   `TryGet` bilan tekshirish `GetValueAsync` ning istisnosidan
    ///   qutqaradi: <see cref="IAutoRecordingScheduler.EnqueueAsync"/>
    ///   shartnomasi HECH QACHON istisno ko'tarmaslikni talab qiladi va
    ///   bu yerda u dars boshlash so'rovini yiqitardi.
    /// </summary>
    private async Task<(bool Enabled, string? ShadowGroups)> ReadTogglesAsync(CancellationToken ct)
    {
        if (!SettingsRegistry.TryGet(SettingsRegistry.Keys.RecordingsTrackPipelineEnabled, out var enabled)
            || !SettingsRegistry.TryGet(SettingsRegistry.Keys.RecordingsTrackPipelineShadowGroups, out var shadow))
        {
            return (Enabled: false, ShadowGroups: null);
        }

        var resolved = await settings.ResolveManyAsync([enabled, shadow], ct).ConfigureAwait(false);

        // ★ `bool.TryParse` MUVAFFAQIYATSIZ BO'LSA `false`: bazaga qo'lda
        //   yozilgan "1" yoki "ha" yangi quvurni YOQMAYDI. Registr
        //   yozishda buni allaqachon to'sadi, lekin `AppSettings` jadvali
        //   qo'lda ham tahrirlanadi.
        var isEnabled = bool.TryParse(resolved[0].Value?.Trim(), out var parsed) && parsed;

        return (isEnabled, resolved[1].Value);
    }

    /// <summary>
    /// Solishtiruv ro'yxatini o'qiydi: <c>"7, 12"</c> ko'rinishidagi matn.
    ///
    /// 🔴 HECH QACHON ISTISNO KO'TARMAYDI. Bu qiymat admin panelidan
    /// qo'lda kiritiladi va u yerda vergul, probel yoki tasodifiy harf
    /// bo'lishi mutlaqo real. Yaroqsiz bo'lak jimgina TASHLANADI (log'da
    /// ogohlantirish bilan), qolganlari esa o'qiladi — bitta xato belgi
    /// tufayli butun dars boshlash so'rovi yiqilmasin.
    ///
    /// ★ GURUH MAVJUDLIGI TEKSHIRILMAYDI — ATAYLAB. "Noma'lum id" shunchaki
    /// hech bir guruhga mos kelmaydi, ya'ni u O'ZIDAN e'tiborsiz qoladi;
    /// mavjudlikni tekshirish esa dars boshlash yo'liga yana bitta so'rov
    /// qo'shardi va hech narsani o'zgartirmasdi.
    /// </summary>
    /// <returns>
    /// <c>Contains</c> — ro'yxatda shu guruh bormi.
    /// <c>Invalid</c> — o'qib bo'lmagan bo'laklar (vergul bilan) yoki <c>null</c>.
    /// </returns>
    private static (bool Contains, string? Invalid) ParseShadowGroups(string? raw, long groupId)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (false, null);

        var contains = false;
        List<string>? invalid = null;

        var parts = raw.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                if (id == groupId)
                    contains = true;

                continue;
            }

            (invalid ??= []).Add(part);
        }

        return (contains, invalid is null ? null : string.Join(", ", invalid));
    }
}
