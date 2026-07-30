namespace Zinnur.Infrastructure.Persistence;

/// <summary>
/// ISH JARAYONIDA o'zgartiriladigan sozlama — kalit/qiymat qatori.
///
/// ★ NIMA UCHUN DOMAIN ENTITY EMAS: bu biznes tushunchasi emas, balki
/// SAQLASH usuli. Domain "chegara 540 000" degan faktni bilishi shart emas —
/// unga qiymat <c>IFinanceSettingsStore</c> port'i orqali beriladi. Shuning
/// uchun sinf Infrastructure ichida qoladi va <c>IApplicationDbContext</c>
/// da <c>DbSet</c> sifatida OCHILMAYDI: Application qatlami bu jadval
/// borligini bilmaydi.
///
/// ★ NIMA UCHUN UMUMAN JADVAL (konfiguratsiya emas): chegara va qamrovni
/// o'quv bo'limi boshlig'i o'zgartiradi (tariflar ko'tarilganda). Buning
/// uchun reliz kutish yoki serverga kirish noto'g'ri bo'lardi. Eski tizimda
/// ham aynan shunday edi (<c>settings</c> jadvali + sozlamalar sahifasi).
///
/// Qiymat SATR: tur kalitga qarab talqin qilinadi. Bu ataylab — jadval
/// kelajakda boshqa sozlamalarni ham ko'taradi va har biri uchun yangi
/// ustun qo'shish (ya'ni migratsiya) kerak bo'lmaydi.
/// </summary>
public sealed class AppSetting
{
    /// <summary>Birlamchi kalit, masalan <c>payment_block_threshold</c>.</summary>
    public required string Key { get; set; }

    public required string Value { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Oxirgi marta kim o'zgartirgani (audit izi <c>PaymentAudits</c> da ham qoladi).</summary>
    public long? UpdatedById { get; set; }
}
