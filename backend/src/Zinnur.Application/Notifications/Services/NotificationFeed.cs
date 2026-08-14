using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Notifications.Dtos;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Notifications.Services;

/// <summary>
/// <see cref="INotificationFeed"/> ning amalga oshirilishi. HTTP haqida
/// HECH NARSA bilmaydi.
/// </summary>
public sealed class NotificationFeed(IApplicationDbContext db, TimeProvider clock) : INotificationFeed
{
    /// <summary>
    /// Bir so'rovda ko'pi bilan shuncha qator.
    ///
    /// ★ 50 — <c>MessagePageDto</c> dagi 100 dan IKKI BAROBAR KICHIK va bu
    /// ataylab: qo'ng'iroqcha ochilganda ochiladigan ro'yxat ~8 qator
    /// balandlikda ko'rinadi. Chat tarixi esa yuqoriga cheksiz suriladi.
    /// </summary>
    private const int MaxTake = 50;

    private const int DefaultTake = 20;

    /// <inheritdoc />
    public async Task<NotificationPageDto> ListAsync(
        long userId,
        long? beforeId = null,
        bool unreadOnly = false,
        int take = DefaultTake,
        CancellationToken ct = default)
    {
        var size = Math.Clamp(take, 1, MaxTake);

        var rows = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);

        if (unreadOnly) rows = rows.Where(n => n.ReadAt == null);

        // ★ KURSOR `Id` BO'YICHA, `CreatedAt` bo'yicha EMAS — sabab
        // `NotificationConfiguration` da: ustoz 50 ta ishni ketma-ket
        // baholaganda bir necha qator AYNI millisekundda yoziladi va vaqt
        // kursori ularni ajrata olmasdi (qator tushib qolardi yoki takror
        // chiqardi).
        if (beforeId is { } cursor) rows = rows.Where(n => n.Id < cursor);

        // BITTA ORTIQCHA qator olamiz — `HasMore` ni QO'SHIMCHA `COUNT`
        // so'rovisiz aniqlash uchun (`GroupChatService` dagi bilan bir xil
        // usul). Ikkinchi so'rov sahifalash bilan poygaga ham tushardi.
        var page = await rows
            .OrderByDescending(n => n.Id)
            .Take(size + 1)
            .Select(n => new NotificationDto(
                n.Id,
                n.Kind,
                n.Title,
                n.Body,
                n.EntityId,
                n.ReadAt != null,
                n.CreatedAt))
            .ToListAsync(ct);

        var hasMore = page.Count > size;
        if (hasMore) page.RemoveAt(page.Count - 1);

        return new NotificationPageDto(
            page,
            hasMore,
            // ★ Kursor OXIRGI (eng eski) qatorning Id'si — u KEYINGI
            // sahifada `Id < cursor` shartiga tushadi, ya'ni chegaradagi
            // qator ikki marta ko'rinmaydi.
            hasMore && page.Count > 0 ? page[^1].Id : null,
            await UnreadAsync(userId, ct));
    }

    /// <inheritdoc />
    public async Task<NotificationUnreadDto> UnreadCountAsync(
        long userId, CancellationToken ct = default) =>
        new(await UnreadAsync(userId, ct));

    /// <inheritdoc />
    public async Task<NotificationReadResultDto> MarkReadAsync(
        long userId, IReadOnlyCollection<long>? ids, CancellationToken ct = default)
    {
        // 🔴 FILTR HAR DOIM `UserId` DAN BOSHLANADI. Id'lar klientdan
        // keladi, ya'ni ularga ishonib bo'lmaydi: bu shart bo'lmasa har kim
        // istalgan odamning bildirishnomasini "o'qildi" qilib, uni
        // ekranidan yo'q qila olardi.
        var rows = db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null);

        if (ids is { Count: > 0 })
        {
            // `Distinct` — klient bir Id'ni ikki marta yuborsa `IN` ro'yxati
            // shishmasin (yuzlab takror Id real bo'lgan holat: ro'yxat
            // bo'ylab bosib chiqilganda).
            var wanted = ids.Distinct().ToArray();
            rows = rows.Where(n => wanted.Contains(n.Id));
        }

        var affected = await rows.ToListAsync(ct);

        var now = clock.GetUtcNow();
        var marked = 0;

        foreach (var row in affected)
        {
            // Domain metodi: takrorda `false` qaytaradi va `ReadAt` ni
            // QAYTA YOZMAYDI (sabab `Notification.MarkRead` izohida).
            if (row.MarkRead(now)) marked++;
        }

        if (marked > 0) await db.SaveChangesAsync(ct);

        return new NotificationReadResultDto(marked, await UnreadAsync(userId, ct));
    }

    /// <summary>
    /// O'qilmaganlar soni — `(UserId, ReadAt, CreatedAt)` indeksidan
    /// to'liq o'qiladi (jadvalga tushmaydi).
    /// </summary>
    private Task<int> UnreadAsync(long userId, CancellationToken ct) =>
        db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);

    /// <summary>
    /// Entity'dan DTO — <see cref="ListAsync"/> dagi proyeksiya bilan
    /// AYNI shakl.
    ///
    /// ★ NIMA UCHUN <c>public static</c> VA SHU YERDA: hodisa yaratuvchi
    /// (<c>AssignmentService</c>) saqlangan qatorni realtime kanaliga
    /// uzatadi va u REST javobidagi bilan AYNAN bir xil bo'lishi kerak.
    /// Ikki joyda qo'lda yasalsa, ular vaqt o'tib ajralib ketardi va
    /// frontend bitta turdagi obyektni ikki xil tahlil qilishga majbur
    /// bo'lardi (<c>GroupChatMessageDto</c> dagi AYNI qaror).
    /// </summary>
    public static NotificationDto ToDto(Notification row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new NotificationDto(
            row.Id,
            row.Kind,
            row.Title,
            row.Body,
            row.EntityId,
            row.ReadAt is not null,
            row.CreatedAt);
    }
}
