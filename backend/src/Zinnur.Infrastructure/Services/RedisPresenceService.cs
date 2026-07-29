using System.Globalization;
using System.Text.Json;
using StackExchange.Redis;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// Jonli darsdagi ishtirokchilar ro'yxati — Redis HASH tuzilmasida.
///
/// ======================================================================
/// NIMA UCHUN HASH (SET yoki alohida kalitlar emas) — 200 kishi uchun muhim
/// ======================================================================
/// Bitta dars = bitta kalit (`presence:{sessionId}`), maydon = userId,
/// qiymat = JSON. Shundan kelib chiqadigan foydalar:
///
/// 1. `HLEN` — ishtirokchilar SONI O(1) da olinadi. Eski tizimda son
///    "hamma yozuvni o'qib, ro'yxat uzunligini olish" bilan hisoblanardi:
///    har kirish/chiqishda 200 ta JSON tarmoqdan o'tardi va 200 kishilik
///    darsda kirish to'lqinida Redis trafigi kvadratik o'sardi (~40 000 o'qish).
///    Shuning uchun `CountAsync` FAQAT `HashLengthAsync` ishlatadi.
/// 2. `HDEL`/`HSET` — bitta ishtirokchi ustida ishlaganda qolgan 199 tasiga
///    tegilmaydi.
/// 3. `EXPIRE` — butun dars uchun BITTA TTL. Server qulab tushsa yoki dars
///    "yakunlash"siz tashlab ketilsa, ro'yxat o'zi tozalanadi va Redis'da
///    "arvoh" ishtirokchilar qolmaydi (eski tizimning surunkali muammosi).
/// </summary>
public sealed class RedisPresenceService : IPresenceService
{
    private const string KeyPrefix = "presence:";

    /// <summary>
    /// Tashlab ketilgan darsning o'zini-o'zi tozalash muddati.
    /// Dars 80 daqiqa + uzaytirish; 8 soat xavfsiz zaxira.
    /// Har `AddAsync` da yangilanadi, ya'ni faol dars hech qachon o'chmaydi.
    /// </summary>
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(8);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _db;

    public RedisPresenceService(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _db = redis.GetDatabase();
    }

    /// <inheritdoc />
    public async Task AddAsync(long sessionId, PresenceEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ct.ThrowIfCancellationRequested();

        var key = Key(sessionId);

        // BATCH: HSET va EXPIRE bitta paketda ketadi — ikki marta tarmoq
        // aylanishi (RTT) o'rniga bitta. 200 kishi bir daqiqada kirganda
        // bu 400 emas, 200 ta aylanish demakdir.
        var batch = _db.CreateBatch();
        var set = batch.HashSetAsync(key, Field(entry.UserId), Serialize(entry));
        var expire = batch.KeyExpireAsync(key, SessionTtl);
        batch.Execute();

        await Task.WhenAll(set, expire).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(long sessionId, long userId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.HashDeleteAsync(Key(sessionId), Field(userId)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PresenceEntry>> ListAsync(
        long sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // To'liq ro'yxat FAQAT shu yerda o'qiladi — SPEC 6.1 bo'yicha u
        // `JoinSession` javobida bir marta beriladi, broadcast'da EMAS.
        var entries = await _db.HashGetAllAsync(Key(sessionId)).ConfigureAwait(false);

        var result = new List<PresenceEntry>(entries.Length);

        foreach (var item in entries)
        {
            var parsed = Deserialize(item.Value);
            if (parsed is not null)
                result.Add(parsed);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task SetHandRaisedAsync(
        long sessionId, long userId, bool raised, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var key = Key(sessionId);
        var field = Field(userId);

        var current = await _db.HashGetAsync(key, field).ConfigureAwait(false);
        var entry = Deserialize(current);

        // Ishtirokchi ro'yxatda bo'lmasa (chiqib ketgan) — hech nima qilinmaydi.
        // Qo'lini ko'targan "arvoh" yozuv yaratmaymiz.
        if (entry is null)
            return;

        // `record` bo'lgani uchun `with` — o'zgarmas (immutable) yangilash.
        // Read-modify-write poyga ehtimoli bor, lekin bitta foydalanuvchi o'z
        // qo'lini ko'taradi: raqobat yo'q, qulf (WATCH/MULTI) narxi ortiqcha.
        await _db.HashSetAsync(key, field, Serialize(entry with { HandRaised = raised }))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(long sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // O(1). Bu yerda HECH QACHON HashGetAll ishlatilmaydi — sabab
        // sinf izohida (`PresenceChanged` hodisasi har kirish/chiqishda
        // shu sonni yuboradi, ya'ni metod eng ko'p chaqiriladiganlardan biri).
        var length = await _db.HashLengthAsync(Key(sessionId)).ConfigureAwait(false);

        return (int)length;
    }

    /// <inheritdoc />
    public async Task ClearAsync(long sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.KeyDeleteAsync(Key(sessionId)).ConfigureAwait(false);
    }

    private static RedisKey Key(long sessionId) =>
        string.Create(CultureInfo.InvariantCulture, $"{KeyPrefix}{sessionId}");

    private static RedisValue Field(long userId) =>
        userId.ToString(CultureInfo.InvariantCulture);

    private static RedisValue Serialize(PresenceEntry entry) =>
        JsonSerializer.Serialize(entry, SerializerOptions);

    private static PresenceEntry? Deserialize(RedisValue value)
    {
        var raw = (string?)value;
        if (raw is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<PresenceEntry>(raw, SerializerOptions);
        }
        catch (JsonException)
        {
            // Formati o'zgargan eski yozuv — butun ro'yxatni yiqitmaymiz.
            return null;
        }
    }
}
