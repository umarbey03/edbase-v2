using Zinnur.Domain.Enums;

namespace Zinnur.Application.Enrollment.Dtos;

/// <summary>
/// Landing sahifadagi «Ariza qoldirish» formasining tanasi
/// (<c>POST /api/v1/applications</c>) — ANONIM.
///
/// ══════════════════════════════════════════════════════════════════════
/// 🔴 BU RO'YXATDAN O'TISH EMAS VA HISOB YARATMAYDI.
///
/// So'rov <c>Users</c> jadvaliga UMUMAN tegmaydi. U faqat o'quv bo'limi
/// panelida bitta qator yaratadi. Sabab va tahdid tahlili —
/// <see cref="Zinnur.Domain.Entities.EnrollmentApplication"/> izohida.
///
/// ★ MAYDONLAR ATAYLAB KAM: har qo'shimcha majburiy maydon to'ldirilmagan
///   formalar ulushini oshiradi, qolgan ma'lumotni esa operator
///   qo'ng'iroqda baribir so'raydi.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Phone">
/// XOM ko'rinish. Normalizatsiya SERVERDA, <c>User.NormalizePhone</c>
/// bilan — ya'ni kirish oqimi bilan AYNI metod.
/// </param>
/// <param name="Course">Qiziqtirgan yo'nalish — ERKIN MATN, katalogga havola EMAS.</param>
public sealed record CreateEnrollmentApplicationRequest(
    string FullName,
    string Phone,
    string? Course,
    string? Note);

/// <summary>
/// Arizaning boshqaruv panelidagi ko'rinishi.
///
/// 🔴 TELEFON RAQAMI BOR, ya'ni bu shakl R27 (kontakt ma'lumoti) doirasiga
/// kiradi va USTOZGA berilmaydi — endpointlar `Academic`/`Admin` bilan
/// yopilgan.
/// </summary>
public sealed record EnrollmentApplicationDto(
    long Id,
    string FullName,
    string Phone,
    string? Course,
    string? Note,
    EnrollmentApplicationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? HandledAt,
    string? HandledByName,
    string? Comment);

/// <summary>Ro'yxat filtri.</summary>
/// <param name="Status"><c>null</c> — hamma holat.</param>
/// <param name="Search">Ism yoki telefon bo'yicha qidiruv (<c>null</c> — filtrsiz).</param>
public sealed record EnrollmentApplicationListParams(
    EnrollmentApplicationStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>
/// Holatni o'zgartirish (<c>PUT /api/v1/applications/{id}</c>).
///
/// ★ ARIZA O'CHIRILMAYDI — sabab entity izohida (konversiya o'lchovi).
/// </summary>
public sealed record UpdateEnrollmentApplicationRequest(
    EnrollmentApplicationStatus Status,
    string? Comment);
