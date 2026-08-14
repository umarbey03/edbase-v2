using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
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
/// So'rov <c>IX_SessionRecordings_SessionId_Id</c> indeksiga tushadi va
/// mavjudlikni tekshiradi (<c>AnyAsync</c>) — qatorni yuklamaydi.
/// </summary>
public sealed class AutoRecordingScheduler(
    IApplicationDbContext db,
    ILiveKitEgress egress,
    IRecordingStorage storage,
    ILogger<AutoRecordingScheduler> logger) : IAutoRecordingScheduler
{
    /// <inheritdoc />
    public async Task<bool> EnqueueAsync(LiveSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        // ── 1. Guruh kaliti ────────────────────────────────────────────
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
        var recordEnabled = session.Group is { } group
            ? group.RecordEnabled
            : await db.Groups
                .AsNoTracking()
                .Where(g => g.Id == session.GroupId)
                .Select(g => g.RecordEnabled)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

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

        // ── 4. Idempotentlik ───────────────────────────────────────────
        var alreadyQueued = await db.SessionRecordings
            .AsNoTracking()
            .AnyAsync(
                r => r.SessionId == session.Id
                  && r.Status != RecordingStatus.Completed
                  && r.Status != RecordingStatus.Failed,
                ct)
            .ConfigureAwait(false);

        if (alreadyQueued)
            return false;

        // ★ `RequestedBy = null` — "TIZIM BOSHLADI". Maydon `SessionRecording`
        //   da allaqachon `nullable` va aynan shu ma'no uchun hujjatlangan,
        //   ya'ni migratsiya KERAK EMAS. Qo'lda boshlangan yozuvda esa u
        //   hamon xodimning Id'sini saqlaydi — "kim yozib olishga qaror
        //   qildi" savoli javobsiz qolmaydi, javob shunchaki ikki xil
        //   bo'ladi: "falonchi xodim" yoki "guruh sozlamasi".
        var recording = new SessionRecording
        {
            SessionId = session.Id,
            RequestedBy = null,
            ObjectKey = storage.BuildObjectKey(session.Id),
        };

        // ⚠️ `SaveChanges` YO'Q — chaqiruvchining tranzaksiyasi (izoh:
        //    `IAutoRecordingScheduler`).
        db.SessionRecordings.Add(recording);

        RecordingLog.AutoQueued(logger, session.Id);

        return true;
    }
}
