using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Settings;
using Zinnur.Infrastructure.Persistence;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// <see cref="ISettingsStore"/> port'ining EF amalga oshirilishi —
/// <c>AppSettings</c> kalit/qiymat jadvali ustida.
///
/// ★ NIMA UCHUN JADVAL PORT ORTIDA: <c>AppSettings</c> ataylab
/// <c>IApplicationDbContext</c> da ochilmagan (sabab <see cref="AppSetting"/>
/// izohida) — Application qatlami bu jadvalning borligini bilmaydi. Shu
/// qaror buzilmasligi uchun use-case'lar faqat port bilan ishlaydi.
///
/// ★ KESH YO'Q (ONGLI TANLOV, eski qarordan meros): qatorlar birlamchi
/// kalit bo'yicha o'qiladi — Postgres uchun eng arzon so'rov. Redis keshi
/// qo'shilsa, sozlama o'zgartirilganda uni bekor qilishni UNUTISH xavfi
/// paydo bo'lardi: xodim raqamni o'zgartirib, "nega ishlamayapti" deb
/// qolardi. Kesh kerak bo'lsa — TTL bilan emas, oshkor bekor qilish bilan.
/// </summary>
public sealed class AppSettingsStore(ApplicationDbContext db, TimeProvider clock) : ISettingsStore
{
    public async Task<IReadOnlyDictionary<string, StoredSetting>> LoadAsync(
        IReadOnlyCollection<string>? storageKeys, CancellationToken ct = default)
    {
        var query = db.AppSettings.AsNoTracking();

        if (storageKeys is not null)
        {
            // Bo'sh ro'yxat — so'rov umuman yubormaslik uchun aniq holat.
            // (`WHERE key IN ()` Postgres uchun ham xato, ham ma'nosiz.)
            if (storageKeys.Count == 0)
                return Empty;

            // Massivga o'girish ATAYLAB: `IReadOnlyCollection<T>` uchun
            // `Contains` — kengaytma metodi, EF esa massiv/ro'yxat ustidagi
            // shaklni ishonchli tarjima qiladi.
            var keys = storageKeys as string[] ?? [.. storageKeys];
            query = query.Where(s => keys.Contains(s.Key));
        }

        var rows = await query
            .Select(s => new { s.Key, s.Value, s.UpdatedAt, s.UpdatedById })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = new Dictionary<string, StoredSetting>(rows.Count, StringComparer.Ordinal);

        foreach (var row in rows)
            result[row.Key] = new StoredSetting(row.Value, row.UpdatedAt, row.UpdatedById);

        return result;
    }

    public async Task SetAsync(
        string storageKey, string value, long? actorId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == storageKey, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = storageKey,
                Value = value,
                UpdatedAt = now,
                UpdatedById = actorId,
            });

            return;
        }

        row.Value = value;
        row.UpdatedAt = now;
        row.UpdatedById = actorId;
    }

    public async Task<bool> RemoveAsync(string storageKey, CancellationToken ct = default)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == storageKey, ct)
            .ConfigureAwait(false);

        if (row is null)
            return false;

        // Qator O'CHIRILADI, "standart qiymat" yozib qo'yilmaydi. Farqi muhim:
        // o'chirilgan qatorda keyinchalik muhitdagi qiymat o'zgarsa u KUCHGA
        // KIRADI. Standart yozib qo'yilsa esa baza uni abadiy ushlab turardi
        // va "nega env o'zgardi-yu, tizim eskisini ishlatyapti?" degan
        // tushunarsiz holat paydo bo'lardi.
        db.AppSettings.Remove(row);
        return true;
    }

    private static readonly IReadOnlyDictionary<string, StoredSetting> Empty =
        new Dictionary<string, StoredSetting>(StringComparer.Ordinal);
}
