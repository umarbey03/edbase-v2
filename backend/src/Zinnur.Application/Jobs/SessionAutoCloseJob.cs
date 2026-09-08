using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.LiveSessions.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Application.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// MUDDATI O'TGAN DARSLARNI AVTO-YAKUNLASH
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ MUAMMO: ustoz "Yakunlash" tugmasini bosmasa dars <c>Live</c> holida
/// abadiy qolardi. Oqibati jimgina va og'ir:
///   • <c>Attendance.Finalize()</c> chaqirilmaydi -> ochiq davomat seansi
///     yopilmaydi va o'quvchi baholanmagan qoladi;
///   • hisobotlar (<c>AttendanceSummaryService</c>, <c>LeaderboardService</c>)
///     va kurs ochilishi (<c>GatingService</c>) FAQAT <c>Ended</c> darslarni
///     sanaydi — ya'ni o'tkazilgan dars statistikaga umuman tushmaydi.
///
/// ── QAMROV: FAQAT HAQIQATAN BOSHLANGAN (<c>Live</c>) DARS ──────────────
///
/// 🔴 BOSHLANMAGAN (<c>Scheduled</c>) DARS ATAYLAB TEGILMAYDI, garchi
/// "muddati o'tgan" degan ta'rifga u ham to'g'ri kelsa-da. Sabab jiddiy:
/// hisobotlar <c>Ended</c> darsni "O'TKAZILGAN dars" deb sanaydi
/// (<c>AttendanceSummaryService</c>: har <c>Ended</c> dars maxrajga
/// qo'shiladi, davomat yozuvi yo'q bo'lsa esa "kelmagan" deb hisoblanadi).
/// Ya'ni umuman o'tkazilmagan darsni <c>Ended</c> qilish HAR o'quvchining
/// davomat foizini jimgina pasaytirardi — bo'lmagan darsga "kelmadi" deb
/// yozilardi. Bu tuzatayotgan muammomizdan ham battar: noto'g'ri hisobot
/// yo'q hisobotdan yomonroq, chunki unga ishonishadi.
///
/// To'g'ri yechim — bunday darsga ALOHIDA holat kerak ("o'tkazilmadi") yoki
/// uni bekor qilish; ikkalasi ham Domain o'zgarishi va biznes qarori.
/// Shuning uchun ular hozircha <c>Scheduled</c> holida qoladi (ya'ni
/// bugungi xatti-harakat SAQLANADI, regressiya yo'q).
///
/// ── QACHON YOPILADI VA NEGA AYNAN SHUNDA ───────────────────────────────
///
/// 🔴 ENG KATTA XAVF — ERTA YOPISH: <see cref="ILiveSessionService.EndAsync"/>
/// xonaga "dars tugadi" xabarini tarqatadi va o'quvchilar ekranidan video
/// yo'qoladi. Hali davom etayotgan darsni uzib qo'yish — halokat. Kech
/// yopish esa faqat hisobotni kechiktiradi.
///
/// Chegara HAR IKKALASIDA HAM <c>EndsAt</c> dan hisoblanadi, bunda
/// <c>EndsAt = ActualStart + rejalashtirilgan davomiylik + uzaytirish</c>.
/// Uzaytirish Domain'da 10 daqiqa bilan CHEGARALANGAN
/// (<see cref="LiveSession.MaxExtendMinutes"/>), ya'ni <c>EndsAt</c> —
/// darsning eng kech RUXSAT ETILGAN tugash payti.
///
/// ★ IKKI CHEGARA BOR, VA TANLOVNI DALIL HAL QILADI:
///
///   1. <c>EndsAt + EmptyRoomGrace</c> (standart 5 daqiqa) — LEKIN FAQAT
///      XONA BO'SH BO'LSA (<see cref="IPresenceService.CountAsync"/> = 0).
///   2. <c>EndsAt + Grace</c> (standart 60 daqiqa) — SHARTSIZ, ya'ni
///      xonada odam bo'lsa ham.
///
/// 🔴 NIMA UCHUN BIRINCHI QOIDA QO'SHILDI (2026-09-08, ustozlarning
/// shikoyati: *"платформада негадр дарс тугамаяпти"*). Ustozlar
/// "Yakunlash" tugmasini bosmaydi — ular shunchaki oynani yopadi. Dars
/// esa AYNI shu sababdan <c>Grace</c> tugaguncha, ya'ni bir soatdan
/// ko'proq vaqt "Hozir efirda" bo'lib turardi: bosh sahifada allaqachon
/// tugagan dars jonli ko'rinardi va o'quvchilar bo'sh xonaga kirardi
/// ("0 / 13 xonada" — dalil o'sha ekranning O'ZIDA edi).
///
/// ★ NIMA UCHUN BU XAVFSIZ: <c>Grace</c> ning butun vazifasi — hali
/// o'qitayotgan ustozni uzib qo'ymaslik. Xona BO'SH bo'lsa uzadigan
/// odamning O'ZI yo'q, ya'ni katta mo'hlat hech kimni himoya qilmaydi,
/// faqat hisobotni va bosh sahifani kechiktiradi.
///
/// ★ DARS CHO'ZILSA NIMA BO'LADI (rejadagi 90 daqiqa -> haqiqatda 120):
/// HECH NARSA. Xonada ustoz (yoki o'quvchi) turgani uchun qisqa qoida
/// UMUMAN ishlamaydi va dars faqat <c>EndsAt + Grace</c> da, ya'ni
/// rejadan ~70 daqiqa (uzaytirish 10 + mo'hlat 60) keyin majburan
/// yopiladi. Bu chegara O'ZGARMADI — u avvaldan shunday edi.
///
/// ── UCH QATLAMLI HIMOYA ────────────────────────────────────────────────
///
///   (a) <c>EndsAt</c> baribir o'tgan bo'lishi SHART — tanaffusga chiqqan
///       guruh yoki hali boshlanmagan dars bu qoidaga umuman tushmaydi;
///
///   (b) 🔴 XONA IKKI MARTA O'LCHANADI, orasida
///       <see cref="SessionAutoCloseSettings.EmptyRoomConfirmDelay"/>
///       (standart 30 soniya). Sabab aniq: ustoz sahifani YANGILAGAN
///       lahzada (F5, tarmoq sakrashi, telefonda ilova fonga o'tishi)
///       presence bir necha soniyaga NOLGA tushadi. Bitta o'lchov bilan
///       aynan o'sha soniyaga tushib qolgan dars UZILIB qolardi va uni
///       QAYTA OCHIB BO'LMASDI (<c>LiveSession.Start</c> yakunlangan
///       darsni rad etadi) — ya'ni ustoz dars o'rtasida quvvatsiz
///       qolardi. Ikkinchi o'lchov shu holatni chetlab o'tadi va u
///       ARZON: ikkala o'lchov ham bitta Redis <c>HLEN</c>;
///
///   (c) presence javob bermasa (Redis) xona "bo'sh" deb HISOBLANMAYDI
///       va dars eski yo'l bilan, <c>Grace</c> dan keyin yopiladi.
///
/// Ikkala chegara ham SOZLANADIGAN
/// (<c>Jobs:SessionAutoClose:GraceMinutes</c>,
/// <c>Jobs:SessionAutoClose:EmptyRoomGraceMinutes</c>): to'g'ri qiymat
/// markazning ish tartibiga bog'liq va uni yangi image yig'masdan
/// tuzatish kerak bo'lishi mumkin.
///
/// ── NIMA QILINMAYDI ────────────────────────────────────────────────────
///
/// ⚠️ <c>Cancelled</c> DARSGA TEGILMAYDI. So'rov uni umuman tanlamaydi
/// (faqat <c>Live</c> tanlanadi), va tanlagan taqdirda ham
/// <see cref="LiveSession.End"/> uni rad etadi (topilgan va test bilan
/// qulflangan bug tuzatmasi): bekor qilingan darsni "Ended" qilish bekor
/// qilish faktini o'chirib tashlardi va bo'lmagan dars uchun davomat
/// yozardi.
///
/// ⚠️ <c>Ended</c> DARSGA QAYTA TEGILMAYDI — amal IDEMPOTENT.
///
/// ⚠️ QO'LDA QO'YILGAN DAVOMAT QAYTA HISOBLANMAYDI. Buni Domain kafolatlaydi:
/// <c>Attendance.Finalize()</c> ichida <c>if (IsManual) return;</c>. Ya'ni
/// ustoz qo'lda "Absent" qo'ygan bo'lsa, avto-yakunlash uni "Present" ga
/// O'ZGARTIRMAYDI.
///
/// ── NIMA UCHUN BAZAGA O'ZI YOZMAYDI ────────────────────────────────────
///
/// Yakunlash use-case'i (<see cref="ILiveSessionService.EndAsync"/>) shu
/// yerda CHAQIRILADI. Bazaga to'g'ridan-to'g'ri yozilsa
/// <c>ILiveSessionNotifier</c> chetlab o'tilardi va o'quvchilar ekranida
/// dars tugagani KO'RINMASDI — bu port izohida ochiq ogohlantirilgan
/// ("broadcast controller'da bo'lsa o'sha yo'l jimgina xabarsiz qolardi").
/// </summary>
public sealed class SessionAutoCloseJob(
    IApplicationDbContext db,
    ILiveSessionService liveSessions,
    IPresenceService presence,
    TimeProvider clock,
    SessionAutoCloseSettings settings,
    ILogger<SessionAutoCloseJob> logger) : IScheduledJob
{
    /// <inheritdoc />
    public string Name => "session-auto-close";

    /// <inheritdoc />
    public TimeSpan Interval => settings.Interval;

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        var actorId = await JobActor.ResolveAsync(db, ct).ConfigureAwait(false);

        if (actorId is null)
        {
            JobLog.NoSystemActor(logger, Name);
            return JobRunResult.Nothing;
        }

        var now = clock.GetUtcNow();

        var candidates = await OverdueLiveAsync(now, ct).ConfigureAwait(false);

        if (candidates.Count == 0)
            return JobRunResult.Nothing;

        var closed = 0;
        var skipped = 0;

        async Task CloseAsync(CloseCandidate candidate)
        {
            try
            {
                await liveSessions.EndAsync(candidate.Id, actorId.Value, ct).ConfigureAwait(false);
                closed++;
                JobLog.SessionClosed(logger, candidate.Id, candidate.Reason);
            }
            catch (Exception ex) when (ex is DomainException or NotFoundException
                                          or ConflictException or ForbiddenException)
            {
                // Biznes qoidasi rad etdi — bu KUTILGAN holat va boshqa
                // darslarga taalluqli emas. Eng ehtimolli sabab: shu qisqa
                // oraliqda dars bekor qilindi yoki qo'lda yakunlandi.
                // Kontekstda o'zgargan yozuv qolmaydi (Domain saqlashdan
                // OLDIN rad etadi), shuning uchun keyingi darsga xavfsiz
                // o'tamiz.
                skipped++;
                JobLog.SessionSkipped(logger, candidate.Id, ex.Message);
            }
        }

        // ── 1) SHARTSIZ: to'liq mo'hlat o'tgan darslar ────────────────────
        //
        // Bu yerda kutish YO'Q — xonada odam bo'lsa ham yopiladi (eski,
        // o'zgarmagan xatti-harakat).
        foreach (var candidate in candidates.Where(c => !c.RequiresEmptyRoom))
        {
            ct.ThrowIfCancellationRequested();

            await CloseAsync(candidate).ConfigureAwait(false);
        }

        // ── 2) BO'SH XONA: BIRINCHI o'lchov ──────────────────────────────
        //
        // Xonada odam bo'lsa dars shu yerda tushib qoladi va to'liq
        // `Grace` tugaguncha kutadi. Bu "o'tkazib yuborildi" EMAS
        // (hisoblagichga tushmaydi): darsning navbati hali kelmagan.
        var pending = new List<CloseCandidate>();

        foreach (var candidate in candidates.Where(c => c.RequiresEmptyRoom))
        {
            ct.ThrowIfCancellationRequested();

            if (await IsRoomEmptyAsync(candidate.Id, ct).ConfigureAwait(false))
                pending.Add(candidate);
        }

        if (pending.Count == 0)
            return new JobRunResult(closed, skipped);

        // ── 3) TASDIQLASH: kutamiz va QAYTA o'lchaymiz ───────────────────
        //
        // 🔴 KUTISH BITTA — har dars uchun emas. Jonli darslar soni bir
        // vaqtda 0–3 ta, ya'ni ular baribir bir necha soniya ichida
        // o'lchanadi; har biriga alohida kutish esa vazifani (va u bilan
        // birga qulfni) keraksiz uzoq ushlab turardi.
        if (settings.EmptyRoomConfirmDelay > TimeSpan.Zero)
            await Task.Delay(settings.EmptyRoomConfirmDelay, clock, ct).ConfigureAwait(false);

        foreach (var candidate in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (!await IsRoomEmptyAsync(candidate.Id, ct).ConfigureAwait(false))
            {
                // Xonaga qaytib kirishdi — demak birinchi o'lchov sahifa
                // yangilanishiga yoki tarmoq sakrashiga to'g'ri kelgan.
                // Dars TEGILMAYDI.
                JobLog.RoomRefilled(logger, candidate.Id);
                continue;
            }

            await CloseAsync(candidate).ConfigureAwait(false);
        }

        return new JobRunResult(closed, skipped);
    }

    /// <summary>
    /// Jonli, lekin muddati o'tgan darslar.
    ///
    /// ★ IKKI BOSQICHLI TANLOV. Bazada faqat QO'POL filtr bajariladi
    /// (<c>ActualStart &lt;= now - EmptyRoomGrace</c>), aniq qoida esa
    /// xotirada — <see cref="LiveSession.IsOverdue"/> orqali. Sabab:
    /// <c>EndsAt</c> hisoblanuvchi xossa (<c>Math.Max</c> va uzaytirish
    /// bilan) va uni EF SQL'ga TARJIMA QILA OLMAYDI. Qoidani SQL'da
    /// qaytadan yozish esa ikki nusxa demakdir: Domain o'zgarsa, ular
    /// jimgina ajralib ketardi va dars noto'g'ri paytda yopilardi.
    ///
    /// Qo'pol filtr HAQIQATAN qo'pol emas: <c>EndsAt &gt;= ActualStart</c>
    /// bo'lgani uchun u kerakli qatorlarning HAMMASINI o'z ichiga oladi
    /// (ya'ni bironta ham dars tushib qolmaydi), ortiqchasi esa xotirada
    /// chetlanadi.
    ///
    /// ★ QO'POL FILTR KICHIK MO'HLAT BILAN OLINADI
    /// (<c>EmptyRoomGrace &lt;= Grace</c>, buni <c>JobsOptions</c>
    /// kafolatlaydi): shu tufayli BITTA so'rov ikkala qoidaning ham
    /// nomzodlarini qamrab oladi. Tartib <c>ActualStart</c> bo'yicha —
    /// ya'ni <see cref="SessionAutoCloseSettings.BatchSize"/> to'lib
    /// qolsa ham, eng eskilari (shartsiz yopiladiganlari) BIRINCHI
    /// bo'lib tushadi va xona bo'shligini kutayotganlar ularni
    /// SIQIB CHIQARMAYDI.
    /// </summary>
    private async Task<IReadOnlyList<CloseCandidate>> OverdueLiveAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        var hardCutoff = now - settings.Grace;
        var emptyCutoff = now - settings.EmptyRoomGrace;

        var live = await db.LiveSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Live
                     && s.ActualStart != null
                     && s.ActualStart <= emptyCutoff)
            .OrderBy(s => s.ActualStart)
            .Take(settings.BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // `IsOverdue(now - X)` == `now >= EndsAt + X`.
        return live
            .Where(s => s.IsOverdue(emptyCutoff))
            .Select(s => new CloseCandidate(s.Id, RequiresEmptyRoom: !s.IsOverdue(hardCutoff)))
            .ToList();
    }

    /// <summary>
    /// Xonada hozir hech kim qolmaganmi (presence, ya'ni Redis).
    ///
    /// 🔴 XATO "BO'SH" DEB HISOBLANMAYDI. Presence javob bermasa (Redis
    /// uzilgan) va biz uni bo'sh deb qabul qilsak, AYNAN o'sha daqiqada
    /// davom etayotgan darsni uzib qo'yardik — ya'ni nosozlik jimgina
    /// halokatga aylanardi. Shuning uchun bunday holatda dars ESKI yo'l
    /// bilan, to'liq <see cref="SessionAutoCloseSettings.Grace"/> dan
    /// keyin yopiladi.
    ///
    /// ⚠️ Bu YAGONA joy emas: <c>LiveSessionService.CountOnlineAsync</c>
    /// ham presence xatosini yutadi va ro'yxatni ochishda davom etadi —
    /// ikkalasida ham qoida bir xil: presence — YORDAMCHI dalil, u
    /// yiqilganda tizim ishlashda davom etadi.
    /// </summary>
    private async Task<bool> IsRoomEmptyAsync(long sessionId, CancellationToken ct)
    {
        try
        {
            return await presence.CountAsync(sessionId, ct).ConfigureAwait(false) == 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            JobLog.PresenceUnavailable(logger, ex, sessionId);
            return false;
        }
    }

    /// <summary>
    /// Yopishga nomzod dars va uning uchun QAYSI qoida ishlayotgani.
    /// </summary>
    /// <param name="RequiresEmptyRoom">
    /// <c>true</c> — dars <c>EndsAt + EmptyRoomGrace</c> dan o'tgan, lekin
    /// hali <c>EndsAt + Grace</c> ga yetmagan; ya'ni uni yopish uchun xona
    /// BO'SH bo'lishi SHART.
    /// </param>
    private sealed record CloseCandidate(long Id, bool RequiresEmptyRoom)
    {
        /// <summary>
        /// Log'dagi sabab. Ikki qoida AJRATILADI: prod'da "nega bu dars
        /// aynan hozir yopildi" degan savolga javob faqat shu satrdan
        /// topiladi.
        /// </summary>
        public string Reason => RequiresEmptyRoom
            ? "muddati o'tgan va xona bo'sh"
            : "muddati o'tgan jonli dars";
    }
}

/// <summary>
/// Avto-yakunlash chegaralari.
///
/// ★ NIMA UCHUN ALOHIDA YOZUV (record) VA <c>IOptions</c> EMAS: Application
/// qatlami konfiguratsiya tizimini BILMAYDI (u WebApi'ning ishi). Qiymatlar
/// DI ro'yxatidan o'tkazishda uzatiladi — <c>IOutboxDispatcher</c> ga paket
/// hajmi va muddat uzatilgani bilan bir xil naqsh. Shu tufayli vazifani
/// testda istalgan chegaralar bilan yurgizish mumkin.
/// </summary>
/// <param name="Grace">
/// Dars ruxsat etilgan tugash paytidan (<c>EndsAt</c>) keyin qancha kutiladi
/// — SHARTSIZ yopish uchun (xonada odam bo'lsa ham).
/// </param>
/// <param name="EmptyRoomGrace">
/// XONA BO'SH bo'lganda <c>EndsAt</c> dan keyin qancha kutiladi. Sezilarli
/// darajada kichik (standart 5 daqiqa): uzib qo'yiladigan odamning o'zi
/// yo'q. ⚠️ <c>Grace</c> DAN KATTA BO'LMASLIGI SHART — aks holda qo'pol
/// SQL filtri shartsiz yopiladigan darslarni tashlab ketardi; buni
/// <c>JobsOptions</c> kafolatlaydi.
/// </param>
/// <param name="EmptyRoomConfirmDelay">
/// Xona bo'sh chiqqach, YOPISHDAN OLDIN qancha kutib qayta o'lchanadi.
/// Sahifa yangilanishi (F5) yoki tarmoq sakrashi presence'ni bir necha
/// soniyaga nolga tushiradi; bitta o'lchov bilan aynan o'sha lahzaga
/// tushgan dars uzilib qolardi va uni qayta ochib bo'lmasdi.
/// <c>TimeSpan.Zero</c> — kutish yo'q (testlar uchun).
/// </param>
/// <param name="BatchSize">Bir yurishda ko'pi bilan nechta dars.</param>
/// <param name="Interval">Ikki yurish orasidagi masofa.</param>
public sealed record SessionAutoCloseSettings(
    TimeSpan Grace,
    TimeSpan EmptyRoomGrace,
    TimeSpan EmptyRoomConfirmDelay,
    int BatchSize,
    TimeSpan Interval);
