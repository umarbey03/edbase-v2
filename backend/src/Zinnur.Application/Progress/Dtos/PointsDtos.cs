using Zinnur.Domain.Enums;

namespace Zinnur.Application.Progress.Dtos;

/// <summary>
/// "Mening ballim" — o'quvchining oylik ball xulosasi (2026-10-23).
/// </summary>
/// <param name="Period">Oy, <c>YYYY-MM</c>.</param>
/// <param name="Total">Oydagi jami ball (manfiyga tushmaydi).</param>
/// <param name="Rank">Guruhdagi o'rni; ball yo'q yoki guruh yo'q bo'lsa <c>null</c>.</param>
/// <param name="GroupName">Asosiy guruh nomi.</param>
/// <param name="Streak">Ketma-ket qatnashilgan darslar (hozirgi holat).</param>
/// <param name="LessonsToNextBonus">Keyingi ketma-ketlik bonusigacha qolgan darslar.</param>
/// <param name="NextBonus">O'sha bonus miqdori.</param>
/// <param name="Breakdown">Tur bo'yicha yig'indi (faqat o'quvchining O'ZIGA ko'rsatiladi).</param>
/// <param name="Recent">Oyning oxirgi yozuvlari, yangisi birinchi.</param>
public sealed record MyPointsDto(
    string Period,
    int Total,
    int? Rank,
    string? GroupName,
    int Streak,
    int LessonsToNextBonus,
    int NextBonus,
    IReadOnlyList<PointsBreakdownDto> Breakdown,
    IReadOnlyList<PointEventDto> Recent);

public sealed record PointsBreakdownDto(PointKind Kind, int Count, int Points);

/// <param name="Title">Nima uchun: guruh/dars, vazifa, test yoki dars nomi.</param>
public sealed record PointEventDto(PointKind Kind, int Delta, DateTimeOffset OccurredAt, string? Title);
