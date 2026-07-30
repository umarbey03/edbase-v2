using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Payments.Dtos;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Finance;

namespace Zinnur.Application.Payments.Services;

/// <summary>
/// Qarzdorlik darvozasi — izoh <see cref="IPaymentBlockService"/> da.
///
/// ★ QARZ AYNAN QANDAY HISOBLANADI: ochiq (<c>Due</c>/<c>Partial</c>)
/// oylarning QOLGAN qismi yig'indisi — <c>Amount − PaidAmount</c>.
/// Eski tizim qisman to'langan oyni ham TO'LIQ qarz deb sanardi, ya'ni
/// 540 000 dan 500 000 to'lagan o'quvchi hamon 540 000 qarzdor ko'rinib
/// bloklanardi. Hisob bazada (<c>SUM</c>) bajariladi: yozuvlarni xotiraga
/// tortish 100 mingta qatorda javobni sekinlashtirardi.
///
/// <c>(StudentId, Status)</c> indeksi shu so'rov uchun ataylab qo'yilgan.
/// </summary>
public sealed class PaymentBlockService(
    IApplicationDbContext db,
    IFinanceSettingsStore settings) : IPaymentBlockService
{
    public async Task<PaymentBlockDto> EvaluateAsync(
        long studentId, PaymentBlockScope requested, CancellationToken ct = default)
    {
        var student = await db.Users.AsNoTracking()
            .Where(u => u.Id == studentId)
            .Select(u => new { u.Id, Exempt = EF.Property<bool>(u, PaymentFields.Exempt) })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(User), studentId);

        var current = await settings.GetAsync(ct);
        var debt = await DebtOfAsync(db, studentId, ct);

        var blocked = PaymentBlockPolicy.IsBlocked(
            debt,
            current.BlockThreshold,
            current.BlockScope,
            requested,
            student.Exempt,
            current.Enforce);

        return new PaymentBlockDto(
            studentId,
            blocked,
            debt,
            current.BlockThreshold,
            current.BlockScope,
            requested,
            student.Exempt,
            current.Enforce,
            blocked ? BuildReason(debt, current.BlockThreshold, requested) : null);
    }

    public async Task EnsureAllowedAsync(
        long studentId, PaymentBlockScope requested, CancellationToken ct = default)
    {
        var status = await EvaluateAsync(studentId, requested, ct);

        // Xabar FOYDALANUVCHIGA ko'rinadi (403 matni ko'rsatiladi), shuning
        // uchun unda uch narsa bor: qancha qarz, chegara qancha va NIMA QILISH.
        // "Ruxsat yo'q" degan quruq matn qo'ng'iroqlar oqimini keltirardi.
        if (status.Blocked)
            throw new ForbiddenException(status.Reason!);
    }

    /// <summary>
    /// Ochiq oylar bo'yicha jami qarz. <c>internal</c> — <c>PaymentService</c>
    /// ham AYNAN shu hisobni ishlatadi (ikki joyda ikki xil formula
    /// bo'lmasin: hisobda 540 000, blokda 0 chiqishi mumkin edi).
    /// </summary>
    internal static async Task<decimal> DebtOfAsync(
        IApplicationDbContext db, long studentId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        return await db.Payments.AsNoTracking()
            .Where(p => p.StudentId == studentId
                     && (p.Status == PaymentStatus.Due || p.Status == PaymentStatus.Partial))
            .SumAsync(p => p.Amount - p.PaidAmount, ct);
    }

    private static string BuildReason(decimal debt, decimal threshold, PaymentBlockScope requested)
    {
        var target = requested switch
        {
            PaymentBlockScope.Video => "video darslarga kirish",
            PaymentBlockScope.Live => "jonli darsga kirish",
            _ => "platformadan foydalanish",
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"To'lov qarzi {debt:0} so'm — ruxsat etilgan chegara {threshold:0} so'm. Shu sababli {target} vaqtincha yopilgan. To'lovni amalga oshiring yoki o'quv bo'limiga murojaat qiling.");
    }
}
