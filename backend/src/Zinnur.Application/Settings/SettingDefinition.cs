namespace Zinnur.Application.Settings;

/// <summary>
/// Sozlama TURI — interfeys qanday maydon chizishini SHU qiymat aytadi.
///
/// ★ NIMA UCHUN <c>String</c>/<c>Integer</c>/<c>Boolean</c> deb ATALMAGAN:
/// analizator qoidasi CA1720 ("identifikator tur nomini o'z ichiga olmasin")
/// aynan shu so'zlarni taqiqlaydi, loyihada esa ogohlantirish = xato.
/// Bundan tashqari bu nomlar UI tilida ham aniqroq: <c>Toggle</c> —
/// belgilash katagi, <c>Choice</c> — ochiluvchi ro'yxat, <c>Secret</c> —
/// maskalangan maydon.
/// </summary>
public enum SettingValueKind
{
    /// <summary>Erkin matn (bir qatorli).</summary>
    Text = 0,

    /// <summary>Butun son.</summary>
    Number = 1,

    /// <summary>Pul yoki o'nlik son. Alohida tur, chunki chegara 540000.50 bo'lishi mumkin.</summary>
    Money = 2,

    /// <summary>Ha/yo'q kaliti.</summary>
    Toggle = 3,

    /// <summary>Cheklangan ro'yxatdan bitta qiymat (<see cref="SettingDefinition.Choices"/>).</summary>
    Choice = 4,

    /// <summary>
    /// SIR. Javobda hech qachon to'liq qaytmaydi va auditga yozilmaydi.
    /// </summary>
    Secret = 5,
}

/// <summary>
/// Sozlamaning ASOSIY MANBAI — "kim oxirgi so'zni aytadi" degan qoida.
///
/// ★ QAROR (eng muhimi): muhit o'zgaruvchisi — BOSHLANG'ICH qiymat,
/// baza — USTUN. Ya'ni bazada qator bo'lsa u ishlatiladi, bo'lmasa
/// konfiguratsiyadagi (env / appsettings) qiymat, u ham bo'lmasa
/// registrdagi standart. Sababi: ish jarayonida o'zgaradigan qiymat uchun
/// reliz kutish yoki serverga kirish noto'g'ri.
///
/// ★ LEKIN HAMMA UCHUN EMAS. Ba'zi kalitlar bazadan boshqarilsa tizim
/// o'zini o'zi qulflab qo'yadi yoki xavfsizlik buziladi — ular
/// <see cref="Environment"/> deb belgilanadi va BAZA UMUMAN O'QILMAYDI
/// (qo'lda qator qo'shilsa ham e'tiborsiz qoladi). Bu ataylab yozishda
/// emas, O'QISHDA to'siladi: aks holda bazaga kirgan hujumchi qatorni
/// qo'shib qo'yishi kifoya bo'lardi.
/// </summary>
public enum SettingSource
{
    /// <summary>Baza ustun (env — boshlang'ich qiymat). Panelidan tahrirlanadi.</summary>
    Database = 0,

    /// <summary>FAQAT konfiguratsiya/muhit. Panel ko'rsatadi, lekin o'zgartira olmaydi.</summary>
    Environment = 1,
}

/// <summary>
/// Joriy qiymat AMALDA qayerdan kelgani. <see cref="SettingSource"/> —
/// SIYOSAT ("qayerdan kelishi kerak"), bu esa FAKT ("qayerdan keldi").
///
/// NIMA UCHUN AJRATILGAN: panelda "bu qiymat hali o'zgartirilmagan,
/// muhitdan kelyapti" va "bu qiymat panelda o'zgartirilgan" holatlari
/// boshqacha ko'rinishi kerak. Bittasida "standartga qaytarish" tugmasi
/// umuman ma'nosiz (qaytadigan joyi yo'q).
/// </summary>
public enum SettingOrigin
{
    /// <summary>Registrdagi standart qiymat (na bazada, na konfiguratsiyada bor).</summary>
    Default = 0,

    /// <summary>Konfiguratsiya/muhit o'zgaruvchisi.</summary>
    Environment = 1,

    /// <summary>Bazadagi qator — ya'ni kimdir paneldan o'zgartirgan.</summary>
    Database = 2,

    /// <summary>
    /// 🔴 SHOSHILINCH muhit o'zgaruvchisi bazadagi qiymatni USTIDAN
    /// YOZGAN (<c>SettingDefinition.OverrideConfigurationKey</c>).
    ///
    /// ★ NIMA UCHUN <see cref="Environment"/> DAN ALOHIDA QIYMAT:
    /// ikkalasi ham "muhitdan keldi" bo'lsa-da, MA'NOSI qarama-qarshi.
    /// <c>Environment</c> — "baza hali to'ldirilmagan, boshlang'ich
    /// qiymat ishlayapti" (normal holat). Bu esa — "bazada qiymat BOR,
    /// lekin u ATAYLAB chetlab o'tilyapti" (avariya holati). Panel
    /// ikkalasini bir xil ko'rsatsa, operator tizim shoshilinch rejimda
    /// turganini umuman bilmasdi va o'zgaruvchini olib tashlashni
    /// unutardi.
    ///
    /// ⚠️ Yangi qiymat OXIRIGA qo'shildi — JSON'da satr sifatida chiqadi
    /// (`origin: "EnvironmentOverride"`), ya'ni mavjud klient buzilmaydi,
    /// lekin frontend tipiga qo'shilishi kerak.
    /// </summary>
    EnvironmentOverride = 3,
}

/// <summary>
/// Matn uchun QO'SHIMCHA format tekshiruvi. Turdan alohida, chunki
/// "manzil" ham, "vaqt zonasi" ham matn — farqi faqat tekshiruvda.
/// </summary>
public enum SettingFormat
{
    /// <summary>Qo'shimcha tekshiruv yo'q.</summary>
    None = 0,

    /// <summary>Absolyut <c>http(s)</c> / <c>ws(s)</c> manzil.</summary>
    Url = 1,

    /// <summary>IANA vaqt zonasi identifikatori (<c>Asia/Tashkent</c>).</summary>
    TimeZone = 2,

    /// <summary>
    /// Telegram bot tokeni: <c>&lt;raqamlar&gt;:&lt;kalit&gt;</c>, bo'shliqsiz.
    ///
    /// ★ NIMA UCHUN QO'SHILDI: token endi PANELDAN yoziladi. Nusxa-joylashtirishda
    /// tushib qolgan probel yoki yarim ko'chirilgan token Bot API tomonida
    /// <c>401 Unauthorized</c> beradi va bot JIMGINA ishlamay qoladi — xato
    /// hech qayerda ko'rinmaydi. Shakl tekshiruvi bu xatoni saqlash paytida,
    /// foydalanuvchi ko'z o'ngida ushlaydi.
    /// </summary>
    TelegramToken = 3,

    /// <summary>
    /// Telegram webhook siri: <c>A-Z a-z 0-9 _ -</c> belgilaridan iborat.
    /// Telegram <c>setWebhook</c> boshqa belgilarni QABUL QILMAYDI — sir
    /// panelda saqlanib, Telegram tomonida esa o'rnatilmay qolardi.
    /// </summary>
    TelegramSecret = 4,
}

/// <summary>
/// Sozlamalar guruhi — panel shu bo'yicha bo'limlarga bo'linadi.
///
/// ⚠️ Qiymatlar bazaga YOZILMAYDI (jadvalda faqat kalit va qiymat), lekin
/// JSON'da SATR sifatida chiqadi — ya'ni yangi guruh mavjud klientni
/// buzmaydi. Baribir FAQAT oxiriga qo'shiladi: panel guruhlarni e'lon
/// TARTIBIDA chizadi.
/// </summary>
public enum SettingGroup
{
    General = 0,
    Finance = 1,
    Telegram = 2,
    LiveKit = 3,
    Storage = 4,
    Security = 5,

    /// <summary>
    /// O'QUV KONTENTI chegaralari (dars videosi va imtihon rasmi hajmi).
    ///
    /// ★ NIMA UCHUN <see cref="Storage"/> GA QO'SHILMADI: u guruhda OMBORGA
    /// ULANISH ma'lumotlari (manzil, bucket, kalit-sirlar) turadi va ularga
    /// administrator faqat ombor ko'chirilganda tegadi. Fayl hajmi chegarasi
    /// esa O'QUV qarori — "bir darsga qanchalik katta video ruxsat etiladi"
    /// degan savolga o'quv bo'limi javob beradi. Bir bo'limda bo'lsa, hajm
    /// chegarasini o'zgartirmoqchi bo'lgan xodim yonida ombor sirlarini
    /// ko'rib turardi.
    /// </summary>
    Content = 6,

    /// <summary>
    /// GURUH CHATI — tarixni avtomatik tozalash siyosati.
    ///
    /// ★ NIMA UCHUN ALOHIDA BO'LIM: bu yerdagi kalitlar boshqa hech bir
    /// bo'limga o'xshamaydi — ular MA'LUMOTNI DOIMIY O'CHIRADI. Qolgan
    /// hamma sozlama xatti-harakatni o'zgartiradi (chegara, manzil, kalit)
    /// va noto'g'ri qiymat qaytarib olinadi. Bu ikkitasi esa qaytarib
    /// bo'lmaydigan amalni boshqaradi, ya'ni ular yonida "diqqat" matni
    /// turishi va ular boshqa sozlamalar orasida KO'ZGA TASHLANMASDAN
    /// qolib ketmasligi kerak.
    /// </summary>
    Chat = 7,
}

/// <summary>
/// BITTA sozlamaning to'liq metama'lumoti.
///
/// ★ NIMA UCHUN BU KODDA, BAZADA EMAS: registr bazada bo'lsa, yangi kalit
/// SQL bilan qo'shilardi — ya'ni kod ko'rigidan o'tmasdi, tur va chegara
/// tekshiruvisiz qolardi va tizimda hech kim o'qimaydigan "yetim" kalitlar
/// paydo bo'lardi. Kodda bo'lsa: yangi sozlama = commit = ko'rik, va
/// noma'lum kalit UMUMAN mavjud bo'la olmaydi (<c>404</c>).
/// </summary>
public sealed record SettingDefinition
{
    private readonly string? _storageKey;

    /// <summary>
    /// Ommaviy identifikator — URL'da va API'da AYNAN shu ishlatiladi
    /// (<c>finance.block_threshold</c>). Kichik harf, nuqta va pastki chiziq.
    /// </summary>
    public required string Key { get; init; }

    public required SettingGroup Group { get; init; }

    /// <summary>Panelda ko'rinadigan nom (o'zbekcha).</summary>
    public required string DisplayName { get; init; }

    /// <summary>NIMA UCHUN kerakligi — panelda maydon ostidagi izoh.</summary>
    public required string Description { get; init; }

    public required SettingValueKind Kind { get; init; }

    public required SettingSource Source { get; init; }

    /// <summary>
    /// Konfiguratsiyadagi yo'l (<c>Jwt:Secret</c>). <see cref="SettingSource.Environment"/>
    /// uchun — YAGONA manba; <see cref="SettingSource.Database"/> uchun —
    /// baza bo'sh bo'lgandagi boshlang'ich qiymat.
    /// </summary>
    public string? ConfigurationKey { get; init; }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// 🔴 SHOSHILINCH USTIDAN YOZISH KALITI ("break-glass")
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Bo'sh bo'lmasa VA konfiguratsiyada shu kalit ostida qiymat bo'lsa —
    /// U BAZADAGI QATORDAN USTUN turadi. Ya'ni odatiy qoida
    /// (<c>baza -&gt; muhit -&gt; standart</c>) TESKARI aylanadi.
    ///
    /// ★ NIMA UCHUN KERAK BO'LIB QOLDI — O'LIK HALQA (2026-08-13):
    ///
    /// Email va parol bilan kirish olib tashlangach, tizimga kirishning
    /// HAR IKKALA yo'li ham Telegram bot tokeniga tayanadi:
    ///   • Mini App — <c>initData</c> imzosi shu token bilan tekshiriladi;
    ///   • telefon + kod — kod shu bot orqali yuboriladi.
    ///
    /// Token esa BAZADA va uni faqat <c>Admin</c> o'zgartira oladi.
    /// Demak: token xato yozilsa → hech kim kira olmaydi → tokenni
    /// tuzatadigan panel ham o'sha kirish ortida qoladi. Bu — faqat
    /// <c>psql</c> bilan tiklanadigan to'liq ishdan chiqish, tunning
    /// istalgan soatida.
    ///
    /// Bu kalit shu halqani uzadi: operator muhit o'zgaruvchisini
    /// qo'yib, konteynerni qayta ishga tushiradi — buzuq baza qatori
    /// e'tiborsiz qoladi va tizim yana ochiladi.
    ///
    /// ★ NIMA UCHUN <see cref="SettingSource.Environment"/> GA
    ///   O'TKAZILMADI: u holda tokenni PANELDAN o'zgartirish umuman
    ///   mumkin bo'lmasdi, ya'ni "token o'g'irlansa serverga kirmasdan
    ///   almashtiraman" degan asosiy foyda yo'qolardi. Bu yerda esa
    ///   odatiy holat o'zgarmaydi: o'zgaruvchi QO'YILMAGUNCHA baza ustun.
    ///
    /// ⚠️ QO'YILGAN BO'LSA PANEL MAYDONI QULFLANADI va sababi
    ///    ko'rsatiladi (<c>SettingsService.ToDto</c>) — aks holda admin
    ///    qiymatni o'zgartirib, "saqlandi" degan javob olib, tizim esa
    ///    eski qiymat bilan ishlayverardi. Bu registrdagi eng qattiq
    ///    qoidaning buzilishi bo'lardi: jimgina yolg'on.
    /// </summary>
    public string? OverrideConfigurationKey { get; init; }

    /// <summary>
    /// Ustidan yozish kuchga kirganda panelda ko'rsatiladigan matn.
    /// <see cref="OverrideConfigurationKey"/> bilan BIRGA to'ldiriladi
    /// (registr tekshiruvi buni talab qiladi).
    /// </summary>
    public string? OverrideReason { get; init; }

    /// <summary>
    /// <c>AppSettings</c> jadvalidagi qator kaliti. Odatda <see cref="Key"/>
    /// bilan bir xil, LEKIN moliya kalitlari uchun ESKI TIZIM nomi saqlanadi
    /// (<c>payment_block_threshold</c>) — ma'lumot ko'chirish skripti eski
    /// <c>settings</c> jadvalidan qiymatlarni AYNAN o'sha nom bilan ko'chiradi.
    /// </summary>
    public string StorageKey
    {
        get => _storageKey ?? Key;
        init => _storageKey = value;
    }

    /// <summary>Na bazada, na konfiguratsiyada bo'lmaganda ishlatiladigan qiymat.</summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary><see cref="SettingValueKind.Choice"/> uchun ruxsat etilgan qiymatlar.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>Son uchun eng kichik ruxsat etilgan qiymat.</summary>
    public decimal? Minimum { get; init; }

    /// <summary>Son uchun eng katta ruxsat etilgan qiymat.</summary>
    public decimal? Maximum { get; init; }

    /// <summary>
    /// Matn uzunligi chegarasi. Standart 500 — bu <c>AppSettings.Value</c>
    /// ustunining uzunligi, ya'ni undan uzun qiymat baribir bazaga sig'maydi
    /// va xatoni foydalanuvchi tushunarli 400 bilan olgani yaxshi.
    /// </summary>
    public int MaxLength { get; init; } = ValueColumnLength;

    /// <summary>
    /// Eng qisqa ruxsat etilgan uzunlik. <c>0</c> — chegara yo'q.
    ///
    /// ★ NIMA UCHUN KERAK BO'LIB QOLDI: kalitlar bazadan boshqarilgach,
    /// "TO'LDIRILGAN BO'LSA — MA'NOLI BO'LSIN" tekshiruvini ishga tushish
    /// paytidagi <c>ValidateOnStart</c> emas, YOZISH paytidagi validatsiya
    /// bajaradi. Ikkita aniq holat: (1) <c>LiveKit:ApiSecret</c> HS256 kaliti
    /// 32 baytdan qisqa bo'lsa jonli dars butunlay o'chardi va buni faqat
    /// birinchi dars boshlanganda bilardik; (2) bo'sh <c>ApiKey</c> yoki bo'sh
    /// <c>Bucket</c> integratsiyani jimgina "sozlanmagan" holatga tushirardi.
    /// </summary>
    public int MinLength { get; init; }

    public SettingFormat Format { get; init; } = SettingFormat.None;

    /// <summary>
    /// Panelda ko'rsatiladigan "nima uchun o'zgartirib bo'lmaydi" matni.
    /// <see cref="SettingSource.Environment"/> uchun MAJBURIY — foydalanuvchi
    /// "nega bu maydon o'chirilgan?" deb so'ramasin.
    /// </summary>
    public string? ReadOnlyReason { get; init; }

    /// <summary>Qiymat sirmi (maskalanadi, auditga yozilmaydi).</summary>
    public bool IsSecret => Kind == SettingValueKind.Secret;

    /// <summary>Paneldan o'zgartirsa bo'ladimi.</summary>
    public bool IsEditable => Source == SettingSource.Database;

    /// <summary><c>AppSettings.Value</c> ustunining uzunligi (EF konfiguratsiyasi bilan bir xil).</summary>
    public const int ValueColumnLength = 500;
}
