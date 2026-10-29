using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// Mustaqil o'quvchining BITTA sotib olingan davri (2026-10-23).
///
/// ★ HAR TO'LOV — ALOHIDA QATOR: uzaytirish mavjud qatorni o'zgartirmaydi,
///   yangisini avvalgisining oxiridan boshlab qo'shadi. Shu tufayli to'lovlar
///   tarixi va "kim, qachon, qancha qabul qildi" o'z-o'zidan audit izi.
/// ★ KIRISH HUQUQI — <see cref="StartsAt"/> ≤ hozir &lt; <see cref="EndsAt"/>
///   va bekor qilinmagan qator bor bo'lsa.
/// </summary>
public class SubscriptionPeriod : BaseEntity
{
    public const int MaxNoteLength = 500;

    public long StudentId { get; set; }

    public User? Student { get; set; }

    /// <summary>Qaysi rejadan; reja keyin o'zgarsa ham shu qatordagi narx va kurs saqlanadi.</summary>
    public long? PlanId { get; set; }

    public SubscriptionPlan? Plan { get; set; }

    public long CourseId { get; set; }

    public Course? Course { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    /// <summary>Qabul qilingan summa (so'm).</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// To'lov usuli. <c>null</c> — o'quvchi BALANSIDAN to'langan (2026-10-29, Q-4):
    /// kassaga yangi pul tushmagan, shuning uchun "naqd" yoki "karta" deb yozish
    /// kassirni yo'q to'lovni qidirishga majbur qilardi.
    /// </summary>
    public PaymentMethod? Method { get; set; }

    /// <summary>
    /// Davr sotilgan paytdagi tarifda interaktiv darslik bormidi (2026-10-29, P2-3).
    ///
    /// ★ <see cref="Amount"/> KABI MUZLATILADI: tarifdagi bayroq keyin o'zgarsa,
    ///   allaqachon sotilgan davrlarning huquqi orqaga qarab o'zgarmasin. Darslikka
    ///   kirish AYNAN shu ustunga qaraydi, tarifga emas.
    /// </summary>
    public bool IncludesInteractiveBook { get; set; }

    public string? Note { get; set; }

    public long CreatedById { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public long? CancelledById { get; set; }

    public string? CancelReason { get; set; }

    public bool IsCancelled => CancelledAt is not null;

    public bool CoversNow(DateTimeOffset now) => !IsCancelled && StartsAt <= now && now < EndsAt;

    /// <summary>
    /// Davrning ISHLATILMAGAN qiymati (2026-10-29, loyiha egasi qarorlari Q-1, Q-2):
    /// <c>Amount × qolgan_vaqt / jami_vaqt</c>, butun so'mgacha PASTGA yaxlitlanadi.
    ///
    /// ★ PASTGA, chunki markaz o'quvchiga olganidan KO'P qaytarmasligi kerak:
    ///   yuqoriga yaxlitlash har almashtirishda 1 so'mdan "yaratib" yuborardi.
    /// ★ Vaqt TICK'larda (kunlarda emas): 31 va 28 kunlik oylar o'z uzunligi
    ///   bo'yicha hisoblanadi va kun o'rtasida almashtirish ham to'g'ri chiqadi.
    /// ★ Hali boshlanmagan davr — to'liq summa; tugagan yoki bekor qilingan — 0.
    /// </summary>
    public decimal UnusedValue(DateTimeOffset now)
    {
        if (IsCancelled || Amount <= 0 || now >= EndsAt)
            return 0m;
        if (now <= StartsAt)
            return Amount;

        var total = (EndsAt - StartsAt).Ticks;
        var remaining = (EndsAt - now).Ticks;
        return Math.Floor(Amount * remaining / total);
    }

    /// <summary>
    /// Joriy davrni HOZIR yopadi (bekor qilmaydi): ishlatilgan qismi tarixda
    /// "ishlatildi" bo'lib qoladi, qolgani esa chaqiruvchi tomonidan pulga aylanadi.
    /// </summary>
    public void CloseAt(DateTimeOffset now)
    {
        if (!CoversNow(now) || now <= StartsAt)
            throw new DomainException("Faqat boshlangan va hali tugamagan davr yopiladi.");

        EndsAt = now;
        UpdatedAt = now;
    }
}
