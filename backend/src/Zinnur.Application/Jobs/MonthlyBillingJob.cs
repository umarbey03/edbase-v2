using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Payments.Dtos;
using Zinnur.Application.Payments.Services;

namespace Zinnur.Application.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// OYLIK TO'LOV YOZUVLARINI AVTOMATIK OCHISH
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ MUAMMO: yozuvlarni xodim QO'LDA ochardi. Unutsa — qarz umuman
/// hisoblanmaydi va oy oxirida markaz pul yo'qotadi; ikki marta bossa —
/// qarz ikki barobar ko'rinadi. Ikkalasi ham eski tizimda sodir bo'lgan.
///
/// ── NIMA UCHUN BU YERDA YANGI MANTIQ YO'Q ──────────────────────────────
///
/// Butun ish <see cref="IPaymentService.OpenPeriodAsync"/> da va u
/// ALLAQACHON idempotent: mavjud yozuvlar oldindan o'qib olinadi va jimgina
/// o'tkazib yuboriladi (<c>AlreadyOpen</c>), oxirgi himoya esa
/// <c>UX_Payments_StudentId_GroupId_Period</c> unikal indeksi. Tarif,
/// chegirma va balansdan avtomatik yopish qoidalari ham o'sha yerda va
/// jonli sinovdan o'tgan. Bu vazifa faqat "qachon" degan savolga javob
/// beradi — "qanday" degan savolga emas.
///
/// ── VAQT MINTAQASI ─────────────────────────────────────────────────────
///
/// 🔴 "Oy boshi" MAHALLIY vaqtda hisoblanadi va buni ham servisning O'ZI
/// qiladi: <c>OpenPeriodRequest.Period = null</c> berilsa
/// <c>ParsePeriodOrCurrent</c> markaz zonasidagi (<c>App:TimeZone</c>,
/// standart <c>Asia/Tashkent</c>) joriy oyni oladi. Server UTC'da ishlaydi,
/// ya'ni 1-avgust 00:30 Toshkentda hali 31-iyul UTC'da — vazifa oyni
/// o'zi hisoblaganda hisob BIR OYGA surilib ketardi. Shuning uchun bu yerda
/// sana bilan HECH QANDAY hisob-kitob qilinmaydi.
///
/// ── VAZIFA TEZ-TEZ YURGANDA NIMA BO'LADI ───────────────────────────────
///
/// Hech narsa: ikkinchi yurish <c>Created=0</c> qaytaradi. Aksincha, tez-tez
/// yurish FOYDALI — oy o'rtasida guruhga qo'shilgan o'quvchiga yozuv
/// ochilishi uchun xodimni kutish shart bo'lmaydi.
///
/// ⚠️ TO'LOVDAN OZOD (<c>PaymentExempt</c>) O'QUVCHILAR: ularga ham yozuv
/// ochiladi va bu TO'G'RI. Bayroq "hisob chiqarilmasin" degani emas —
/// u faqat QARZ UCHUN BLOKLASHDAN ozod qiladi (<c>PaymentBlockService</c>).
/// Yozuv ochilmasa markazning haqiqiy hisobi buzilardi: bepul o'qiyotgan
/// o'quvchi hisobotda umuman ko'rinmasdi. Chegirma esa boshqa mexanizm —
/// 100% <c>StudentDiscount</c> summani nolga tushiradi va yozuv ochilishi
/// bilan darhol yopiladi.
/// </summary>
public sealed class MonthlyBillingJob(
    IApplicationDbContext db,
    IPaymentService payments,
    MonthlyBillingSettings settings,
    ILogger<MonthlyBillingJob> logger) : IScheduledJob
{
    /// <inheritdoc />
    public string Name => "monthly-billing";

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

        // `Period = null` -> markaz vaqt zonasidagi JORIY oy (izoh yuqorida).
        // `GroupId = null` -> barcha faol guruhlar.
        var result = await payments
            .OpenPeriodAsync(new OpenPeriodRequest(), actorId.Value, ct)
            .ConfigureAwait(false);

        if (result.Created > 0 || result.MonthsClosedFromBalance > 0)
        {
            JobLog.PeriodOpened(
                logger, result.Period, result.Created,
                result.AlreadyOpen, result.MonthsClosedFromBalance);
        }

        // Tarif topilmagan guruhlar — SOZLASH XATOSI va u ko'rinishi kerak.
        // Faqat BIRINCHI ogohlantirish yoziladi: qolganlari bir xil
        // sababning takrori bo'ladi va logni to'ldirardi.
        if (result.SkippedNoTariff > 0 && result.Warnings.Count > 0)
            JobLog.PeriodWarning(logger, result.SkippedNoTariff, result.Period, result.Warnings[0]);

        return new JobRunResult(
            Processed: result.Created,
            Skipped: result.AlreadyOpen + result.SkippedNoTariff,
            Note: result.Period);
    }
}

/// <summary>
/// Oylik hisob vazifasining sozlamasi. Sabab — <see cref="SessionAutoCloseSettings"/>.
/// </summary>
/// <param name="Interval">Ikki yurish orasidagi masofa.</param>
public sealed record MonthlyBillingSettings(TimeSpan Interval);
