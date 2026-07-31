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
/// yopish esa faqat hisobotni kechiktiradi. Shuning uchun chegara ATAYLAB
/// saxovatli:
///
///   <c>EndsAt + Grace</c> dan keyin, bunda
///   <c>EndsAt = ActualStart + rejalashtirilgan davomiylik + uzaytirish</c>.
///
/// Uzaytirish Domain'da 10 daqiqa bilan CHEGARALANGAN
/// (<see cref="LiveSession.MaxExtendMinutes"/>), ya'ni <c>EndsAt</c> —
/// darsning eng kech RUXSAT ETILGAN tugash payti. Standart
/// <c>Grace = 60 daqiqa</c> — o'sha chegaradan OLTI barobar ko'p, ya'ni
/// hali o'qitayotgan ustozni uzib qo'yish amalda mumkin emas.
///
/// Chegara SOZLANADIGAN (<c>Jobs:SessionAutoClose:GraceMinutes</c>): to'g'ri
/// qiymat markazning ish tartibiga bog'liq va uni yangi image yig'masdan
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

        var candidates = await OverdueLiveIdsAsync(now, ct).ConfigureAwait(false);

        if (candidates.Count == 0)
            return JobRunResult.Nothing;

        var closed = 0;
        var skipped = 0;

        foreach (var id in candidates)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await liveSessions.EndAsync(id, actorId.Value, ct).ConfigureAwait(false);
                closed++;
                JobLog.SessionClosed(logger, id, "muddati o'tgan jonli dars");
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
                JobLog.SessionSkipped(logger, id, ex.Message);
            }
        }

        return new JobRunResult(closed, skipped);
    }

    /// <summary>
    /// Jonli, lekin muddati o'tgan darslar.
    ///
    /// ★ IKKI BOSQICHLI TANLOV. Bazada faqat QO'POL filtr bajariladi
    /// (<c>ActualStart &lt;= now - Grace</c>), aniq qoida esa xotirada —
    /// <see cref="LiveSession.IsOverdue"/> orqali. Sabab: <c>EndsAt</c>
    /// hisoblanuvchi xossa (<c>Math.Max</c> va uzaytirish bilan) va uni EF
    /// SQL'ga TARJIMA QILA OLMAYDI. Qoidani SQL'da qaytadan yozish esa
    /// ikki nusxa demakdir: Domain o'zgarsa, ular jimgina ajralib ketardi
    /// va dars noto'g'ri paytda yopilardi.
    ///
    /// Qo'pol filtr HAQIQATAN qo'pol emas: <c>EndsAt &gt;= ActualStart</c>
    /// bo'lgani uchun u kerakli qatorlarning HAMMASINI o'z ichiga oladi
    /// (ya'ni bironta ham dars tushib qolmaydi), ortiqchasi esa xotirada
    /// chetlanadi.
    /// </summary>
    private async Task<IReadOnlyList<long>> OverdueLiveIdsAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - settings.Grace;

        var live = await db.LiveSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Live
                     && s.ActualStart != null
                     && s.ActualStart <= cutoff)
            .OrderBy(s => s.ActualStart)
            .Take(settings.BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // `IsOverdue(now - Grace)` == `now >= EndsAt + Grace`.
        return live.Where(s => s.IsOverdue(cutoff)).Select(s => s.Id).ToList();
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
/// Dars ruxsat etilgan tugash paytidan (<c>EndsAt</c>) keyin qancha kutiladi.
/// </param>
/// <param name="BatchSize">Bir yurishda ko'pi bilan nechta dars.</param>
/// <param name="Interval">Ikki yurish orasidagi masofa.</param>
public sealed record SessionAutoCloseSettings(
    TimeSpan Grace,
    int BatchSize,
    TimeSpan Interval);
