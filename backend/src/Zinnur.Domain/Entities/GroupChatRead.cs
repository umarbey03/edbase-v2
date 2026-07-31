using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// "OXIRGI O'QILGAN XABAR" BELGISI — o'qilmaganlar sanog'ining ASOSI
/// ========================================================================
///
/// Bitta qator = bitta foydalanuvchining bitta oqimdagi
/// (<c>GroupId</c> + <c>Channel</c>) o'qish chegarasi.
///
/// ★ NIMA UCHUN HAR XABARDA "o'qildi" BAYROG'I EMAS
/// (<see cref="DirectMessage.ReadByStudent"/> dan farqli):
/// shaxsiy yozishmada o'quvchi IKKITA — ikkita bayroq yetadi. Guruh
/// chatida esa o'quvchi 30 ta: har xabar uchun 30 ta qator kerak bo'lardi,
/// ya'ni 1000 xabarli guruh = 30 000 qator va har ochilishda 30 ta
/// <c>UPDATE</c>. Chegara belgisida esa foydalanuvchi boshiga BITTA qator
/// va o'qilmaganlar soni indeksdan hisoblanadigan oddiy <c>COUNT</c>:
///
///   <c>WHERE GroupId=@g AND Channel=@c AND SenderId&lt;&gt;@me AND Id &gt; @lastRead</c>
///
/// ★ NIMA UCHUN <c>Id</c>, VAQT EMAS: <c>bigserial</c> qat'iy o'suvchi va
/// yagona. Vaqt bo'yicha chegara bir xil millisekundda kelgan ikki xabarni
/// ajrata olmasdi va soat surilganda (NTP) chegara orqaga ketardi.
/// </summary>
public class GroupChatRead : BaseEntity
{
    public long GroupId { get; set; }

    public Group? Group { get; set; }

    public GroupChatChannel Channel { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Shu Id gacha (shu Id ham kiradi) hammasi o'qilgan.
    /// <c>0</c> — hali hech nima o'qilmagan.
    /// </summary>
    public long LastReadMessageId { get; set; }

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// Belgini oldinga suradi. ORQAGA HECH QACHON ketmaydi.
    ///
    /// ★ NIMA UCHUN: klient bir necha so'rovni parallel yuborishi mumkin
    /// (ekran ochildi + yangi xabar keldi). Tarmoqda ular TARTIBSIZ yetib
    /// borsa, eski chegarali so'rov keyin kelib, allaqachon o'qilgan
    /// xabarlarni yana "o'qilmagan" qilib qo'yardi — sanoq o'z-o'zidan
    /// ko'payib turgandek ko'rinardi.
    /// </summary>
    /// <returns>Belgi haqiqatan o'zgardimi (idempotentlik uchun).</returns>
    public bool Advance(long messageId, DateTimeOffset now)
    {
        if (messageId < 0)
            throw new DomainException("O'qilgan xabar Id'si manfiy bo'lishi mumkin emas.");

        if (messageId <= LastReadMessageId) return false;

        LastReadMessageId = messageId;
        UpdatedAt = now;
        return true;
    }
}
