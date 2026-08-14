using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Auth.Dtos;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Auth.Services;

/// <summary>
/// Kirish/chiqish use-case'lari.
/// HTTP haqida HECH NARSA bilmaydi — faqat domain xatolarini ko'taradi.
/// </summary>
public sealed class AuthService(
    IApplicationDbContext db,
    IJwtTokenService tokens,
    IAuthStateCache authState) : IAuthService
{
    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var parsed = tokens.ValidateRefreshToken(refreshToken)
            ?? throw new UnauthorizedException("Sessiya muddati tugagan. Qaytadan kiring.");

        var user = await db.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Id == parsed.UserId, ct)
            ?? throw new UnauthorizedException("Foydalanuvchi topilmadi.");

        // Token bekor qilinganmi (rol o'zgargan / chiqilgan / bog'lanish uzilgan)
        if (user.TokenVersion != parsed.TokenVersion)
            throw new UnauthorizedException("Sessiya bekor qilingan. Qaytadan kiring.");

        if (!user.IsActive)
            throw new ForbiddenException("Profil faol emas.");

        return Build(user);
    }

    /// <inheritdoc />
    public async Task<AuthResponse> LoginWithTelegramAsync(
        long telegramUserId, CancellationToken ct = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramId == telegramUserId, ct);

        // 409: bu "kod xato" emas — imzo TO'G'RI, faqat bu Telegram
        // akkaunt hali hech kimga bog'lanmagan. Klient shu kodni ko'rib
        // "avval botda raqamingizni ulashing" ekranini ochadi. 401 bo'lsa
        // klient uni oddiy kirish xatosi deb ko'rsatib, foydalanuvchi nima
        // qilishni bilmay qolardi.
        if (user is null)
        {
            throw new ConflictException(
                "Telegram akkauntingiz profilga bog'lanmagan. "
                + "Avval botda telefon raqamingizni ulashing.");
        }

        // ══════════════════════════════════════════════════════════════
        // ★★ AUDIT X-1 MITIGATSIYASI QAYTA YOZILDI (2026-08-13)
        //
        // ESKI YOZUV (bekor qilindi): "Telegram orqali FAQAT `Student`
        // kiradi; xodimlar email va parol bilan kirishadi." Bu qoida
        // ikkinchi, mustaqil to'siq sifatida turardi.
        //
        // NIMA UCHUN BEKOR QILINDI: email va parol bilan kirish BUTUNLAY
        // olib tashlandi (loyiha egasining qarori). Ya'ni eski izohdagi
        // "xodimlar u yerdan kiradi" jumlasi endi MAVJUD BO'LMAGAN
        // eshikka ishora qilardi. Bu qoida saqlansa xodim uchun kirish
        // yo'li umuman qolmasdi.
        //
        // ★ X-1 ("Telegram orqali istalgan rolni olish") ENDI NIMA BILAN
        //   TO'SILADI — uchta mustaqil qatlam:
        //
        //   1) BOG'LANISH TELEGRAM TASDIG'INI TALAB QILADI. Raqam faqat
        //      `message.contact` orqali keladi va `contact.user_id` xabar
        //      YUBORUVCHISI bilan solishtiriladi
        //      (`TelegramUpdateHandler`). Qo'lda raqam yozish yo'li
        //      UMUMAN yo'q — eski tizimdagi zaiflik AYNAN shu edi.
        //
        //   2) BOT AKKAUNT YARATMAYDI. Raqam bazada oldindan bo'lishi
        //      shart, uni esa faqat o'quv bo'limi kiritadi.
        //
        //   3) QAYTA BOG'LASH — FAQAT ODAM ORQALI. Profilda boshqa
        //      Telegram ID tursa bot uni jimgina almashtirmaydi; eski
        //      bog'lanishni o'quv bo'limi bekor qiladi va bu audit iziga
        //      tushadi (`TelegramUnlinkAudit`).
        //
        // 🔴 HALOL BAHO — NIMA YO'QOLDI: xodim uchun parol IKKINCHI omil
        //    vazifasini bajarardi. Endi u yo'q. Operator raqamni qayta
        //    sotsa (O'zbekistonda odatiy hol) yoki SIM almashtirilsa,
        //    yangi ega xodim profiliga kira oladi. Yagona qarshi chora —
        //    xodim ishdan ketganda profilni DARHOL o'chirish yoki
        //    Telegram'ini uzish. Bu tartib `docs/DEPLOY_UBUNTU.md` da
        //    yozilgan.
        // ══════════════════════════════════════════════════════════════
        if (!user.IsActive)
            throw new ForbiddenException("Profil faol emas. O'quv bo'limi bilan bog'laning.");

        return Build(user);
    }

    /// <inheritdoc />
    public async Task<AuthResponse> LoginWithPhoneAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("Foydalanuvchi topilmadi.");

        // ★ FAOLLIK TEKSHIRUVI SHU YERDA TAKRORLANADI (chaqiruvchi ham
        //   tekshirgan bo'lsa ham). Sabab: bu — token BERILADIGAN joy, va
        //   har eshik uchun bu shartni chaqiruvchiga qoldirish yangi eshik
        //   qo'shilganda uni unutish demakdir. Takrorlash arzon, unutish
        //   esa o'chirilgan xodimni tizimga qaytarardi.
        if (!user.IsActive)
            throw new ForbiddenException("Profil faol emas. O'quv bo'limi bilan bog'laning.");

        return Build(user);
    }

    public async Task LogoutAllAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        user.InvalidateTokens();
        await db.SaveChangesAsync(ct);

        // ★ Keshdagi eski versiya tozalanmasa chiqish 60 sekundgacha kuchga
        // kirmasdi — "chiqdim" bosgan odam hamon ichkarida bo'lardi.
        await authState.InvalidateAsync(userId, ct);
    }

    public async Task<UserDto> GetCurrentAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(User), userId);

        return Map(user);
    }

    private AuthResponse Build(User user) =>
        new(tokens.CreateAccessToken(user), tokens.CreateRefreshToken(user), Map(user));

    // `Phone` — XOM ustun (`PhoneNormalized` emas): foydalanuvchi o'zining
    // odatiy ko'rinishdagi raqamini ko'radi. Sabab va cheklov `UserDto` da.
    private static UserDto Map(User u) =>
        new(u.Id, u.FullName, u.Email, u.Phone, u.Role.ToString());
}
