using Zinnur.Domain.Common;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// MUSTAQIL KURATORLARI RO'YXATI — AVTOMATIK BIRIKTIRISH (2026-11-20)
/// ════════════════════════════════════════════════════════════════════════
///
/// Loyiha egasining talabi: kurator Mustaqil tarifga qo'shilsa, kuratorsiz
/// o'quvchilar unga O'ZI biriktiriladi — sig'imi (standart 100) to'lguncha.
/// To'lganda keyingi o'quvchi ro'yxatdagi KEYINGI kuratorga o'tadi; hammasi
/// to'lsa o'quvchi navbatda qoladi va xodimlarga "yangi kurator qo'shing"
/// eslatmasi boradi. Yangi kurator qo'shilishi bilan navbat unga taqsimlanadi.
///
/// ★ TARTIB — QO'SHILGAN VAQT: birinchi qo'shilgan kurator birinchi to'ladi
///   ("100 tasi birinchisiga, keyingi 100 tasi ikkinchisiga"). Teng taqsimlash
///   EMAS — talab aynan ketma-ket to'ldirishni aytadi.
///
/// ★ SIG'IM FAQAT FAOL OBUNALILARNI SANAYDI: obunasi tugagan o'quvchi joy
///   egallamaydi (u qaytsa, eski kuratori bilan davom etadi — biriktirish
///   o'chmaydi). Qo'lda biriktirish sig'imni chetlab o'tishi mumkin — ataylab:
///   o'quv bo'limi istisno qila olsin; panel "100+" ni qizil bilan ko'rsatadi.
///
/// ★ RO'YXATDAN CHIQARISH mavjud biriktirishlarga TEGMAYDI — faqat yangi
///   o'quvchi unga tushmaydi. Kuratorning o'quvchilarini boshqasiga o'tkazish
///   alohida, ongli amal (panelda ommaviy biriktirish).
/// </summary>
public class SelfStudyCuratorPoolMember : BaseEntity
{
    public const int DefaultCapacity = 100;
    public const int MinCapacity = 1;
    public const int MaxCapacity = 1000;

    /// <summary>Kurator (Assistant yoki Teacher). Ro'yxatda bir marta — unikal indeks.</summary>
    public long CuratorId { get; set; }

    public User? Curator { get; set; }

    /// <summary>Nechta FAOL obunali o'quvchi avtomatik biriktiriladi.</summary>
    public int Capacity { get; private set; } = DefaultCapacity;

    /// <summary>
    /// Yangi o'quvchi qabul qiladimi. Ta'tilga chiqqan kurator ro'yxatdan
    /// o'chirilmasdan vaqtincha to'xtatiladi — navbatdagi o'rni saqlanadi.
    /// </summary>
    public bool IsAccepting { get; set; } = true;

    /// <summary>
    /// Ro'yxatga qo'shgan xodim. Avtomatik biriktirishlar uning nomidan
    /// yoziladi (<c>SelfStudyCuratorAssignment.AssignedById</c>): "kim
    /// biriktirdi" savoliga javob — "qoidani kim o'rnatdi".
    /// </summary>
    public long AddedById { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    public static SelfStudyCuratorPoolMember Create(long curatorId, int? capacity, long addedById, DateTimeOffset now)
    {
        var member = new SelfStudyCuratorPoolMember
        {
            CuratorId = curatorId,
            AddedById = addedById,
            AddedAt = now,
        };
        member.SetCapacity(capacity ?? DefaultCapacity);
        return member;
    }

    public void SetCapacity(int capacity)
    {
        if (capacity is < MinCapacity or > MaxCapacity)
            throw new DomainException($"Sig'im {MinCapacity} dan {MaxCapacity} gacha bo'lishi kerak.");
        Capacity = capacity;
    }
}
