using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Birlik kontentining bitta versiyasi (har import — yangi qator, eskisi
/// o'chirilmaydi), 2026-10-29.
///
/// ★ NEGA <see cref="ContentRaw"/> VA <see cref="Content"/> IKKALASI: Postgres
///   `jsonb` bo'sh joy va kalit tartibini o'zgartiradi, ya'ni undan baytma-bayt
///   xeshni qayta hisoblab bo'lmaydi. Manba haqiqati — <see cref="ContentRaw"/>
///   (fayl matni aynan), `jsonb` faqat qulaylik uchun.
///
/// 🔴 Bu qatorda JAVOB KALITLARI va USTOZ IZOHLARI bor. O'quvchiga u faqat
///   oq ro'yxat proyeksiyasi (`StudentUnitProjector`) orqali chiqadi.
/// </summary>
public class InteractiveUnitVersion : BaseEntity
{
    public const int Sha256Length = 64;
    public const int MaxSourcePathLength = 500;

    public long UnitId { get; set; }

    public InteractiveUnit? Unit { get; set; }

    public int Version { get; set; }

    /// <summary>`dars.json` matni BAYTMA-BAYT (UTF-8 dan dekodlangan).</summary>
    public required string ContentRaw { get; set; }

    /// <summary><see cref="ContentRaw"/> UTF-8 baytlarining SHA-256 (kichik harf, hex).</summary>
    public required string ContentSha256 { get; set; }

    /// <summary><see cref="ContentRaw"/> ning `jsonb` nusxasi.</summary>
    public required string Content { get; set; }

    /// <summary>Ustoz qo'llanmasi (Markdown). FAQAT xodimga.</summary>
    public string? TeacherGuideMd { get; set; }

    public string? TeacherGuideSha256 { get; set; }

    /// <summary>`jsonb`: <c>{ "ab01": "&lt;sha256&gt;", … }</c> — audio id → fayl xeshi.</summary>
    public required string AudioManifest { get; set; }

    /// <summary>`jsonb`: son ko'rsatkichlar (bloklar, mashqlar, test, daftar, audio).</summary>
    public required string Stats { get; set; }

    public string? SourcePath { get; set; }

    public long? ImportRunId { get; set; }
}
