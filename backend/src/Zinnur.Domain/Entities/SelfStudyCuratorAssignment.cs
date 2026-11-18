using System.Linq.Expressions;
using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// MUSTAQIL O'QUVCHINING KURATORI (2026-11-18)
/// ========================================================================
///
/// Mustaqil o'quvchi guruhsiz, ya'ni guruh orqali yuradigan "kim mas'ul"
/// qoidasi (<c>StaffResponsibility</c>) unga hech kimni topmaydi. Shu
/// sababli uning vazifasini faqat o'quv bo'limi ko'rardi. Loyiha egasi:
/// har Mustaqil o'quvchiga BITTA kurator biriktiriladi, u vazifani
/// tekshiradi, 24/7 yozishmada javob beradi va darsni «o'zlashtirdi»
/// deb belgilaydi; uni ZAXIRA kurator to'ldiradi.
///
/// ★ TARIX SAQLANADI: kurator almashganda eski qator O'CHIRILMAYDI, faqat
///   <see cref="EndedAt"/> yoziladi. "Bu darsni kim belgilagan edi, o'sha
///   paytda u kurator edimi?" degan savolga javob shu jadvalda.
/// ★ BIR VAQTDA BITTA FAOL QATOR: bazadagi qisman unikal indeks
///   (<c>EndedAt IS NULL</c>) kafolatlaydi — ikki parallel so'rov ikki faol
///   kuratorni yozib qo'ya olmaydi.
/// </summary>
public class SelfStudyCuratorAssignment : BaseEntity
{
    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>Asosiy kurator: tekshiradi, yozishmada javob beradi.</summary>
    public long CuratorId { get; set; }

    public User? Curator { get; set; }

    /// <summary>
    /// Zaxira kurator (ixtiyoriy): asosiy kurator yo'qligida vazifani
    /// tekshiradi va darsni belgilay oladi.
    /// </summary>
    public long? BackupCuratorId { get; set; }

    public User? BackupCurator { get; set; }

    public long AssignedById { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>Biriktirish tugagan payt; <c>null</c> — hozir faol.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    public long? EndedById { get; set; }

    public bool IsActive => EndedAt is null;

    /// <summary>
    /// Xodim shu o'quvchiga hozir mas'ulmi — ASOSIY yoki ZAXIRA sifatida.
    /// Ifoda bitta joyda: baholash ruxsati, ro'yxatlar va yozishma AYNI
    /// shartni o'qiydi, shuning uchun ular hech qachon ajralib ketmaydi.
    /// </summary>
    public static Expression<Func<SelfStudyCuratorAssignment, bool>> ActiveFor(
        long staffId, bool includeBackup = true) =>
        includeBackup
            ? a => a.EndedAt == null && (a.CuratorId == staffId || a.BackupCuratorId == staffId)
            : a => a.EndedAt == null && a.CuratorId == staffId;

    public static SelfStudyCuratorAssignment Create(
        long studentId,
        long curatorId,
        long? backupCuratorId,
        long assignedById,
        DateTimeOffset now)
    {
        if (studentId <= 0 || curatorId <= 0)
            throw new DomainException("O'quvchi va kurator ko'rsatilishi shart.");
        if (studentId == curatorId || studentId == backupCuratorId)
            throw new DomainException("O'quvchi o'ziga kurator bo'la olmaydi.");
        if (backupCuratorId == curatorId)
            throw new DomainException("Zaxira kurator asosiy kuratordan boshqa odam bo'lishi kerak.");

        return new SelfStudyCuratorAssignment
        {
            StudentId = studentId,
            CuratorId = curatorId,
            BackupCuratorId = backupCuratorId,
            AssignedById = assignedById,
            AssignedAt = now,
            CreatedAt = now,
        };
    }

    /// <summary>Shu biriktirish AYNAN shu juftlikmi (qayta saqlash — no-op).</summary>
    public bool IsSameAs(long curatorId, long? backupCuratorId) =>
        CuratorId == curatorId && BackupCuratorId == backupCuratorId;

    public void End(long actorId, DateTimeOffset now)
    {
        if (EndedAt is not null) return;
        EndedAt = now;
        EndedById = actorId;
        UpdatedAt = now;
    }
}
