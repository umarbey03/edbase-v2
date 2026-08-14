using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ============================================================================
/// GURUH KATEGORIYASI (R21b) — o'quv YO'NALISHI ("ATF", "Grammatika",
/// "CEFR", "IELTS")
/// ============================================================================
///
/// Talab (loyiha egasi): *"guruh category bo'yicha (ATF va grammatika,
/// masalan CEFR yoki IELTS), bu category parametr sifatida guruh uchun
/// qo'shilishi kerak"*.
///
/// ── NIMA UCHUN ENUM EMAS, JADVAL ────────────────────────────────────────
///
/// Talabdagi "masalan" so'zi ro'yxatning OCHIQ ekanini bildiradi, va
/// mahsulot bir nechta o'quv markaziga sotilmoqda — har biri o'z
/// yo'nalishlarini o'zi ataydi. Enum bo'lsa har yangi yo'nalish uchun kod
/// o'zgarishi, migratsiya va deploy kerak bo'lardi; jadval bo'lsa o'quv
/// bo'limi uni paneldan qo'shadi.
///
/// ── ⚠️ <see cref="Course"/> BILAN CHEGARA (ochiq savol, hujjatlashtirilgan) ──
///
/// 🔴 BU IKKALASI HOZIRGI MA'LUMOTDA BIR-BIRINI TAKRORLASHI MUMKIN:
/// <see cref="Course"/> sinfining o'z izohida MISOL sifatida AYNAN "ATF"
/// yozilgan, ya'ni loyiha egasi kategoriyaga bergan birinchi misol allaqachon
/// KURS nomi. Farq faqat MA'NOda:
///
///   • <see cref="Course"/> — KONTENT: modullar, darslar, gating zanjiri,
///     <c>Group.VideoStartLessonId</c> shu daraxtga ishora qiladi. Kursni
///     o'zgartirish o'quvchi KO'RADIGAN materialni o'zgartiradi.
///   • bu sinf — YORLIQ: hech qanday kontenti yo'q, faqat guruhlarni
///     saralash va filtrlash uchun. O'chirilsa (FK <c>SET NULL</c>) birorta
///     dars ham yo'qolmaydi.
///
/// Ya'ni ular AJRALADIGAN holat: bitta "IELTS" kategoriyasi ostida bir necha
/// KURS bo'lishi ("IELTS 6.5 intensiv", "IELTS boshlang'ich"), yoki kursi
/// UMUMAN biriktirilmagan guruh ham kategoriya olishi mumkin (bugungi
/// bazadagi 33 guruhning ko'pi aynan shunday). Agar markazda har kategoriya
/// AYNAN bitta kursga to'g'ri kelsa — ikkalasi haqiqatan takror bo'ladi va
/// birini yig'ishtirish kerak. Bu qaror LOYIHA EGASINIKI; shu sabab bu
/// yerda ochiq yozilgan.
/// </summary>
public class GroupCategory : BaseEntity
{
    /// <summary>Yorliq: "ATF", "Grammatika", "CEFR", "IELTS".</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Ro'yxatdagi tartib (0 dan, zich). <see cref="Course.Position"/> bilan
    /// AYNI naqsh: alifbo tartibi o'quv markazining haqiqiy ustuvorligini
    /// ifodalamaydi ("ATF" birinchi bo'lishi kerak bo'lsa ham "CEFR" dan
    /// keyin turardi).
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Faolmi. 🔴 O'CHIRISH O'RNIGA ARXIVLASH kerak bo'lgan holat:
    /// kategoriya biriktirilgan guruhlar bo'lsa, uni o'chirish ularni
    /// jimgina kategoriyasiz qoldiradi (FK <c>SET NULL</c>). Nofaol
    /// kategoriya yangi guruhga tanlanmaydi, lekin eski guruhlarda
    /// KO'RINIB turadi.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Nom chegarasi — konfiguratsiyadagi <c>HasMaxLength</c> bilan AYNI.</summary>
    public const int MaxNameLength = 100;

    /// <summary>
    /// Invariant. Servis buni undan OLDIN 400 bilan tutadi
    /// (<c>GroupCategoryService.RequireName</c>); bu yerdagi tekshiruv
    /// servisdan tashqari yo'llarni (seed, import, fon vazifasi) qo'riqlaydi.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new DomainException("Kategoriya nomi kiritilishi shart.");

        if (Name.Length > MaxNameLength)
            throw new DomainException("Kategoriya nomi juda uzun.");
    }
}
