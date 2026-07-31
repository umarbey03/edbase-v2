namespace Zinnur.Application.Settings;

/// <summary>Bazada saqlangan bitta sozlama qatori.</summary>
/// <param name="Value">Xom (satr) qiymat — turi registrga qarab talqin qilinadi.</param>
/// <param name="UpdatedAt">Oxirgi o'zgartirilgan vaqt.</param>
/// <param name="UpdatedById">Oxirgi o'zgartirgan xodim.</param>
public sealed record StoredSetting(string Value, DateTimeOffset? UpdatedAt, long? UpdatedById);

/// <summary>
/// ========================================================================
/// SOZLAMA QATORLARI UCHUN PORT
/// ========================================================================
///
/// ★ NIMA UCHUN PORT KERAK: sozlamalar <c>AppSettings</c> jadvalida yotadi,
/// lekin bu jadval ATAYLAB <c>IApplicationDbContext</c> da OCHILMAGAN —
/// Application qatlami uning borligini bilmasligi kerak (sabab
/// <c>AppSetting</c> sinfining izohida). Shu qarorni buzmaslik uchun
/// use-case'lar jadvalga emas, SHU interfeysga tayanadi.
///
/// ★ <c>SaveChanges</c> BU YERDA CHAQIRILMAYDI. Sabab
/// <c>FinanceSettingsStore</c> dagi bilan bir xil: chaqiruvchi qiymat bilan
/// BIRGA audit yozuvini ham qo'shadi va ikkalasini BITTA tranzaksiyada
/// saqlaydi. Aks holda sozlama o'zgarib, audit izi yozilmay qolishi mumkin
/// edi — ya'ni "kim o'zgartirdi?" degan savol javobsiz qolardi.
///
/// ══════════════════════════════════════════════════════════════════════════
/// 🔴 SIRLAR BAZADA SHIFRLANMAGAN SAQLANADI — ONGLI QAROR
///
/// Bu yerda bot tokeni, R2 kalitlari va LiveKit siri yotadi. Ular
/// <c>AppSetting.Value</c> ustunida OCHIQ MATN. Qaror va uning narxi:
///
/// ★ NIMA UCHUN SHIFRLANMAYDI:
///   1) Shifrlash KALIT talab qiladi, kalit esa muhit o'zgaruvchisida
///      bo'lardi (boshqa joyimiz yo'q — KMS/HSM bu loyihada yo'q). Ya'ni
///      serverga kira olgan hujumchi kalitni ham, bazani ham oladi —
///      himoya faqat "baza nusxasi sizib chiqdi" holatida ishlaydi.
///   2) O'sha bitta holat uchun to'lanadigan narx katta: kalit yo'qolsa
///      (konteyner qayta yaratildi, `.env` tiklanmadi) BARCHA sir
///      qaytarib bo'lmaydigan darajada yo'qoladi — ya'ni yangi nosozlik
///      turi paydo bo'ladi va u aynan eng yomon paytda chiqadi.
///   3) Yarim pishgan kriptografiya ("AES + muhitdagi kalit") xavfsizlik
///      TUYG'USINI beradi-yu, haqiqiy xavfni deyarli kamaytirmaydi.
///
/// ★ BUNING O'RNIGA QILINGAN: registrga tanlov QAT'IY cheklangan
/// (<c>SettingsRegistry</c>). Bazaga faqat AYLANTIRIB (rotate) qutulsa
/// bo'ladigan sirlar tushadi: bot tokeni, ombor kalitlari, LiveKit siri —
/// ular sizib chiqsa paneldan bir daqiqada almashtiriladi. Tizimni
/// QULFLAY yoki huquqni KENGAYTIRA oladigan sirlar (JWT imzo kaliti,
/// Postgres ulanish satri) bazaga UMUMAN tushmaydi.
/// Qo'shimcha qatlamlar: sir HTTP javobiga chiqmaydi (<c>SettingMask</c>),
/// auditga yozilmaydi (<c>SettingAuditPolicy</c>), logga tushmaydi.
///
/// ★ OPERATSION TALAB: <c>AppSettings</c> jadvali bo'lgan zaxira nusxa va
/// `pg_dump` fayli SIR hisoblanadi — uni tahlil uchun tashqariga uzatishdan
/// oldin shu jadval tozalanishi kerak.
///
/// ★ AGAR KELAJAKDA SHIFRLASH KERAK BO'LSA: yagona to'g'ri joy — SHU PORT
/// amalga oshirilishi (<c>AppSettingsStore</c>). O'qish/yozish bitta
/// tor joydan o'tadi, ya'ni <c>IDataProtector</c> ni qo'shish uchun
/// chaqiruvchilarning birortasini ham o'zgartirish kerak bo'lmaydi.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Saqlangan qatorlarni o'qiydi.
    /// </summary>
    /// <param name="storageKeys">
    /// Kerakli kalitlar; <c>null</c> — hammasi. Ro'yxat berilishi ATAYLAB:
    /// blok tekshiruvi har so'rovda ishlaydi va unga faqat ikkita kalit kerak.
    /// </param>
    Task<IReadOnlyDictionary<string, StoredSetting>> LoadAsync(
        IReadOnlyCollection<string>? storageKeys,
        CancellationToken ct = default);

    /// <summary>
    /// Qatorni yozadi yoki yangilaydi. <c>SaveChanges</c> CHAQIRILMAYDI —
    /// o'zgarish chaqiruvchining tranzaksiyasida qoladi (izoh yuqorida).
    /// </summary>
    Task SetAsync(string storageKey, string value, long? actorId, CancellationToken ct = default);

    /// <summary>
    /// Qatorni o'chiradi — shundan keyin qiymat muhitdan yoki registrdagi
    /// standartdan olinadi ("standart qiymatga qaytarish" aynan shu).
    /// </summary>
    /// <returns>O'chiriladigan qator bor edimi.</returns>
    Task<bool> RemoveAsync(string storageKey, CancellationToken ct = default);
}

/// <summary>
/// ========================================================================
/// KONFIGURATSIYA (env / appsettings) UCHUN PORT
/// ========================================================================
///
/// Application qatlami <c>IConfiguration</c> ni bilmaydi — bu ASP.NET
/// dunyosidagi tur. Registrga esa "shu kalitning muhitdagi qiymati nima?"
/// degan savol kerak: u boshlang'ich qiymat sifatida ham, faqat o'qish
/// uchun mo'ljallangan kalitlar uchun YAGONA manba sifatida ham ishlatiladi.
/// </summary>
public interface ISettingsEnvironment
{
    /// <summary>
    /// Konfiguratsiyadagi qiymat. Kalit yo'q yoki bo'sh bo'lsa — <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Bo'sh satr ATAYLAB <c>null</c> ga tenglashtiriladi: docker-compose'da
    /// <c>Storage__Bucket=</c> ko'rinishidagi bo'sh o'zgaruvchi juda tez-tez
    /// uchraydi va u "sozlanmagan" degani, "bo'sh qiymat o'rnatilgan" degani emas.
    /// </remarks>
    string? Read(string configurationKey);
}
