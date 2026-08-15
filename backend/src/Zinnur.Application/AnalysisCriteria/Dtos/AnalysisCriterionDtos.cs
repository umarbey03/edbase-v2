namespace Zinnur.Application.AnalysisCriteria.Dtos;

/// <summary>Dars tahlili mezoni — Sozlamalar ro'yxati va tahlil formasi uchun.</summary>
public sealed record AnalysisCriterionDto(
    long Id,
    string Name,
    decimal MaxScore,
    int SortOrder);

/// <summary>Mezon yaratish yoki tahrirlash (UPSERT emas — `POST`/`PUT` ajratilgan, oddiy CRUD).</summary>
public sealed record SaveAnalysisCriterionRequest(
    string Name,
    decimal MaxScore,
    int SortOrder);
