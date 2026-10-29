using Zinnur.Domain.Common;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Darslik audiosi — KONTENT MANZILLI (SHA-256 bo'yicha), 2026-10-29.
///
/// ★ Bir xil fayl (masalan, ikki birlikdagi bir xil bo'g'in) omborga BIR
///   MARTA yuklanadi. Import avval faylni omborga yozadi, keyin bu qatorni —
///   baza hech qachon yo'q faylga ishora qilmaydi.
/// </summary>
public class InteractiveAudio : BaseEntity
{
    public required string Sha256 { get; set; }

    public required string ObjectKey { get; set; }

    public long SizeBytes { get; set; }

    public required string ContentType { get; set; }
}
