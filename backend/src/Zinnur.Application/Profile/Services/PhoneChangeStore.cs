using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Zinnur.Application.Common.Interfaces;

namespace Zinnur.Application.Profile.Services;

/// <summary>
/// <see cref="IPhoneChangeStore"/> — Redis (<see cref="ICacheService"/>) ustida.
///
/// ★ Kalit makoni va `ICacheService` orqali ishlash sababi
/// <c>PhoneLoginCodeStore</c> dagi bilan AYNI: Application qatlami Redis
/// kutubxonasini ko'rmaydi, prefiks esa portning ichida qo'yiladi.
/// </summary>
public sealed class PhoneChangeStore(ICacheService cache) : IPhoneChangeStore
{
    /// <inheritdoc />
    public async Task SaveAsync(PendingPhoneChange pending, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pending);

        /*
          IKKI KALIT — AYNI yozuv.

          ★ NIMA UCHUN NUSXA, nega ikkinchi kalit birinchisiga "ko'rsatkich"
          emas: ko'rsatkich bo'lsa har o'qishda IKKI marta Redis'ga
          borilardi (avval yo'naltirish, keyin yozuv). Yozuv kichkina va
          qisqa umrli, ya'ni nusxaning narxi nolga yaqin.

          ⚠️ IKKALASI BIRGA yangilanadi va BIRGA o'chiriladi — quyidagi
          `RemoveAsync` ham shunday. Faqat bittasi qolib ketsa, bot
          allaqachon tugagan niyat bo'yicha kod yuboraverardi.
        */
        await cache.SetAsync(UserKey(pending.UserId), pending, IPhoneChangeStore.Ttl, ct)
            .ConfigureAwait(false);

        await cache.SetAsync(PhoneKey(pending.PhoneNormalized), pending, IPhoneChangeStore.Ttl, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<PendingPhoneChange?> FindByPhoneAsync(
        string phoneNormalized, CancellationToken ct = default) =>
        cache.GetAsync<PendingPhoneChange>(PhoneKey(phoneNormalized), ct);

    /// <inheritdoc />
    public Task<PendingPhoneChange?> FindByUserAsync(long userId, CancellationToken ct = default) =>
        cache.GetAsync<PendingPhoneChange>(UserKey(userId), ct);

    /// <inheritdoc />
    public async Task RemoveAsync(PendingPhoneChange pending, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pending);

        await cache.RemoveAsync(UserKey(pending.UserId), ct).ConfigureAwait(false);
        await cache.RemoveAsync(PhoneKey(pending.PhoneNormalized), ct).ConfigureAwait(false);
    }

    // ================================================================ ichki

    /// <summary>
    /// Raqamdan kalit bo'lagi — <c>PhoneLoginCodeStore.Slug</c> bilan AYNI
    /// usul.
    ///
    /// 🔴 Raqamning O'ZI kalitga tushmaydi: Redis kalitlari log va
    /// tashxis vositalarida ko'rinadi, telefon raqami esa shaxsiy
    /// ma'lumot.
    /// </summary>
    private static string Slug(string phoneNormalized)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(phoneNormalized ?? string.Empty));
        return Convert.ToHexString(digest)[..16];
    }

    private static string PhoneKey(string phoneNormalized) =>
        string.Create(CultureInfo.InvariantCulture, $"profile:phone-change:phone:{Slug(phoneNormalized)}");

    private static string UserKey(long userId) =>
        string.Create(CultureInfo.InvariantCulture, $"profile:phone-change:user:{userId}");
}
