using Zinnur.Application.Common.Interfaces;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// BCrypt asosidagi parol hash'lash.
///
/// ======================================================================
/// NIMA UCHUN IKKALA METOD HAM `Task.Run` ORQALI
/// ======================================================================
/// BCrypt — ATAYLAB sekin algoritm: work factor 11 da bitta hisob ~250 ms
/// SOF CPU vaqti oladi. U I/O emas, shuning uchun "tabiiy" async yo'li yo'q.
///
/// Eski tizimda `BCrypt.Verify(...)` to'g'ridan-to'g'ri async controller
/// ichida chaqirilardi. Natija: har kirish so'rovi ASP.NET thread pool
/// oqimini 250 ms band qilardi. Dars boshlanishida 200 o'quvchi bir vaqtda
/// kirganda pool tugab, thread pool starvation boshlanardi — SignalR
/// ulanishlari ham, oddiy `/health` ham javob bermay qolardi.
///
/// `Task.Run` bu ishni pul oqimiga (worker) uzatadi: so'rov oqimi darhol
/// bo'shaydi va boshqa so'rovlarga xizmat qiladi. Bu "async theatre" emas —
/// CPU-bound ishni request oqimidan CHIQARISH aynan shu holat uchun to'g'ri.
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    /// <summary>
    /// BCrypt work factor. 11 = 2^11 raund (~250 ms).
    /// 10 dan past — zamonaviy GPU uchun zaif; 12+ esa kirish oqimini sekinlashtiradi.
    /// </summary>
    private const int WorkFactor = 11;

    /// <inheritdoc />
    public Task<string> HashAsync(string password, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        return Task.Run(
            () => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor),
            ct);
    }

    /// <inheritdoc />
    public Task<bool> VerifyAsync(string password, string hash, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(hash))
            return Task.FromResult(false);

        return Task.Run(
            () =>
            {
                try
                {
                    return BCrypt.Net.BCrypt.Verify(password ?? string.Empty, hash);
                }
                catch (BCrypt.Net.SaltParseException)
                {
                    // Bazadagi hash buzilgan yoki boshqa formatda (masalan eski
                    // tizimdan ko'chirilgan). 500 xato o'rniga "parol noto'g'ri"
                    // deymiz — AuthService uni oddiy muvaffaqiyatsiz kirish
                    // sifatida qayta ishlaydi.
                    return false;
                }
            },
            ct);
    }
}
