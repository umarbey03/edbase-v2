namespace Zinnur.Application.Profile.Dtos;

/// <summary>
/// Profil rasmi yuklangandan keyingi javob.
/// </summary>
/// <param name="AvatarUpdatedAt">
/// Kesh buzish uchun vaqt tamg'asi — klient rasm manziliga shu qiymatni
/// qo'shadi (<c>?v=…</c>), aks holda brauzer eski rasmni ko'rsatib
/// turardi (sabab <c>User.AvatarUpdatedAt</c> izohida).
/// </param>
public sealed record AvatarUploadedDto(DateTimeOffset AvatarUpdatedAt);
