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

    /// <summary>
    /// KEYS[1] = ishtirokchilar, KEYS[2] = ulanishlar;
    /// ARGV = connectionId, userId, yozuv JSON, TTL (s).
    ///
    /// ★ <c>HSETNX</c>: foydalanuvchi allaqachon ro'yxatda bo'lsa (ikkinchi
    /// ulanish) yozuvi QAYTA YOZILMAYDI — aks holda qayta ulanish uning
    /// ko'tarilgan qo'lini va kirgan vaqtini jimgina o'chirardi.
    /// </summary>
    private const string AddScript = """
        redis.call('HSET', KEYS[2], ARGV[1], ARGV[2])
        local added = redis.call('HSETNX', KEYS[1], ARGV[2], ARGV[3])
        redis.call('EXPIRE', KEYS[1], ARGV[4])
        redis.call('EXPIRE', KEYS[2], ARGV[4])
        return added
        """;

    /// <summary>
    /// KEYS[1] = ishtirokchilar, KEYS[2] = ulanishlar; ARGV = connectionId, userId.
    /// Foydalanuvchining boshqa ulanishi qolmagan bo'lsagina uni chiqaradi.
    /// <c>HVALS</c> — xonadagi ulanishlar soni (≤ ~400) bo'yicha, Redis ichida.
    /// </summary>
    private const string RemoveScript = """
        redis.call('HDEL', KEYS[2], ARGV[1])
        local values = redis.call('HVALS', KEYS[2])
        for i = 1, #values do
            if values[i] == ARGV[2] then return 0 end
        end
        redis.call('HDEL', KEYS[1], ARGV[2])
        return 1
        """;

    private readonly IDatabase _db;

    public RedisPresenceService(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _db = redis.GetDatabase();
    }

    /// <inheritdoc />
    public async Task<bool> AddAsync(
        long sessionId, PresenceEntry entry, string connectionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        ct.ThrowIfCancellationRequested();

        // Bitta skript = bitta tarmoq aylanishi va atomik natija.
        var added = await _db.ScriptEvaluateAsync(
                AddScript,
                [Key(sessionId), ConnectionsKey(sessionId)],
                [connectionId, Field(entry.UserId), Serialize(entry), (long)SessionTtl.TotalSeconds])
            .ConfigureAwait(false);

        return (long)added == 1;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        long sessionId, long userId, string connectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        ct.ThrowIfCancellationRequested();

        var gone = await _db.ScriptEvaluateAsync(
                RemoveScript,
                [Key(sessionId), ConnectionsKey(sessionId)],
                [connectionId, Field(userId)])
            .ConfigureAwait(false);

        return (long)gone == 1;
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

        await _db.KeyDeleteAsync([Key(sessionId), ConnectionsKey(sessionId)]).ConfigureAwait(false);
    }

    private static RedisKey Key(long sessionId) =>
        string.Create(CultureInfo.InvariantCulture, $"{KeyPrefix}{sessionId}");

    /// <summary>
    /// Ulanishlar xaritasi: maydon = SignalR <c>connectionId</c>, qiymat = userId.
    /// Ishtirokchilar hash'i (<see cref="Key"/>) o'zgarmaydi — <c>HLEN</c>
    /// hamon ODAMLAR sonini beradi, ulanishlar sonini emas.
    /// </summary>
    private static RedisKey ConnectionsKey(long sessionId) =>
        string.Create(CultureInfo.InvariantCulture, $"{KeyPrefix}{sessionId}:conn");

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
