using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Shop;

public interface ISelfSignupService
{
    /// <summary>
    /// Bazada yo'q raqam bilan botga kelgan odamga o'quvchi profili ochadi
    /// va Telegram'ini bog'laydi. Sozlama o'chiq bo'lsa — <c>null</c> (avvalgi
    /// "akkaunt yaratilmaydi" qoidasi kuchda).
    /// </summary>
    Task<User?> TryRegisterAsync(
        long telegramUserId, string? firstName, string? lastName, string? username, string rawPhone,
        CancellationToken ct = default);
}

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// O'ZI RO'YXATDAN O'TISH (2026-10-29, onlayn sotuv)
/// ════════════════════════════════════════════════════════════════════════
///
/// Ilgari bot HECH QACHON akkaunt yaratmasdi (<c>TelegramUpdateHandler</c>,
/// "AKKAUNT YARATILMAYDI"). Loyiha egasi saytda tarif sotishni so'radi —
/// buning uchun begona odam profil ochishi kerak. Qaror sozlama ortida
/// (<c>sales.self_signup_enabled</c>, standart — o'chiq).
///
/// ★ NIMA UCHUN XAVFSIZ:
///   • raqam egaligini Telegram'ning o'zi tasdiqlaydi (<c>contact.user_id ==
///     from.id</c> tekshiruvi chaqiruvchida) — begona raqam bilan profil ochib
///     bo'lmaydi;
///   • rol FAQAT <see cref="UserRole.Student"/>, guruhsiz va obunasiz — u
///     faqat tariflar va o'z profilini ko'radi; darslar to'lov tasdiqlangach
///     ochiladi. Guruh dunyosi va markaz reytingi unga yopiq
///     (<c>SingleCenterScope</c> — "mehmon" auditoriyasi);
///   • mavjud profil (bazada raqam bor) bu yo'lga TUSHMAYDI — qayta sotilgan
///     raqam bilan boshqa o'quvchining profiliga kirish yo'li ochilmaydi.
/// </summary>
public sealed class SelfSignupService(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    ISettingsResolver settings,
    TimeProvider clock,
    ILogger<SelfSignupService> logger) : ISelfSignupService
{
    private const int MaxNameLength = 120;

    public async Task<User?> TryRegisterAsync(
        long telegramUserId, string? firstName, string? lastName, string? username, string rawPhone,
        CancellationToken ct = default)
    {
        if (!SettingsRegistry.TryGet(SettingsRegistry.Keys.SalesSelfSignupEnabled, out var definition))
            return null;

        var resolved = await settings.ResolveAsync(definition, ct);
        if (!(SettingValueParser.TryReadBool(resolved.Value, out var enabled) && enabled))
            return null;

        var name = string.Join(' ', new[] { firstName, lastName }
            .Select(part => part?.Trim())
            .Where(part => !string.IsNullOrEmpty(part)));

        if (name.Length == 0)
            name = "Yangi o'quvchi";
        else if (name.Length > MaxNameLength)
            name = name[..MaxNameLength];

        var user = new User
        {
            FullName = name,

            // Parol bilan kirish yo'q — ustun to'ldiriladi, xolos (`UserService` dagi AYNI naqsh).
            PasswordHash = await hasher.HashAsync(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), ct),
            Role = UserRole.Student,
            IsActive = true,
        };

        user.SetPhone(rawPhone);
        user.LinkTelegram(telegramUserId, username, clock.GetUtcNow());

        db.Users.Add(user);

        // ★ DARHOL SAQLANADI: kirish kodi profil Id'si bilan yoziladi
        //   (`ContinueAfterLinkAsync`), Id esa faqat saqlangach paydo bo'ladi.
        await db.SaveChangesAsync(ct);

        ShopLog.SelfSignup(logger, user.Id);
        return user;
    }
}
