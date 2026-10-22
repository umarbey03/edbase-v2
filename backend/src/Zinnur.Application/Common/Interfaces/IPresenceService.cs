using Zinnur.Application.Common.Models;

namespace Zinnur.Application.Common.Interfaces;

/// <summary>
/// Jonli darsdagi ishtirokchilar ro'yxati — Redis'da (hash) saqlanadi.
///
/// NIMA UCHUN REDIS: ikki API instance bo'lsa, in-memory Dictionary'da
/// har biri o'z ro'yxatini ko'radi va o'quvchilar bir-birini ko'rmaydi.
/// </summary>
public interface IPresenceService
{
    /// <summary>
    /// Ulanishni ro'yxatga qo'shadi.
    ///
    /// ★ ULANISH BO'YICHA (2026-10-22): bitta foydalanuvchining bir nechta
    /// ulanishi bo'lishi mumkin — qayta ulanish (eski ulanish hali
    /// uzilmagan), ikkinchi tab yoki qurilma. Ro'yxatda foydalanuvchi BIR
    /// marta turadi; ulanishlar alohida sanaladi.
    /// </summary>
    /// <returns><c>true</c> — foydalanuvchi ro'yxatda YANGI paydo bo'ldi.</returns>
    Task<bool> AddAsync(
        long sessionId, PresenceEntry entry, string connectionId, CancellationToken ct = default);

    /// <summary>
    /// Ulanishni olib tashlaydi. Foydalanuvchi ro'yxatdan faqat uning
    /// OXIRGI ulanishi yopilganda chiqadi — aks holda qayta ulangan
    /// o'quvchi eski ulanish uzilganda darsda turib "chiqib ketgan"
    /// bo'lib qolardi.
    /// </summary>
    /// <returns><c>true</c> — bu foydalanuvchining oxirgi ulanishi edi.</returns>
    Task<bool> RemoveAsync(
        long sessionId, long userId, string connectionId, CancellationToken ct = default);

    Task<IReadOnlyList<PresenceEntry>> ListAsync(long sessionId, CancellationToken ct = default);

    Task SetHandRaisedAsync(long sessionId, long userId, bool raised, CancellationToken ct = default);

    Task<int> CountAsync(long sessionId, CancellationToken ct = default);

    /// <summary>Dars tugaganda butun ro'yxatni tozalaydi.</summary>
    Task ClearAsync(long sessionId, CancellationToken ct = default);
}
