namespace Zinnur.Application.Notifications.Dtos;

/// <summary>
/// Navbatga qo'yiladigan xabar — use-case shu shaklda beradi.
///
/// ★ MATN TAYYOR HOLDA KELADI (shablon + parametr JSON emas). Sabab:
///
///   1) Yozib olingan matn — DALIL. "Menga bunday xabar kelmagan" degan
///      shikoyatda bazadagi qator aynan yuborilgan matnni ko'rsatadi.
///   2) Shablon KEYIN o'zgarsa, navbatda turgan eski xabar YANGI shablon
///      bilan qayta yasalib ketmaydi. Eski tizimda eslatma yuborish paytida
///      qayta hisoblanardi va bekor qilingan dars haqida xabar ketardi.
///   3) Parametrlarni JSON'da saqlash — bu kalit/qiymat qopchasi: sxema
///      yo'q, tekshiruv yo'q, "qaysi parametr kerak" degan savolga javob
///      faqat koddan topiladi.
///
/// <see cref="TemplateKey"/> baribir saqlanadi — lekin MATN uchun emas,
/// GURUHLASH uchun: "bugun nechta eslatma ketdi", "qaysi tur xabar ko'p
/// yiqilyapti" degan savollarga javob beradi va kelajakda foydalanuvchi
/// o'zi kanal turlarini o'chirib qo'ya olishi uchun kerak bo'ladi.
/// </summary>
public sealed record NotificationRequest
{
    /// <summary>Qaysi kanal orqali yuboriladi.</summary>
    public required NotificationChannel Channel { get; init; }

    /// <summary>
    /// Platforma foydalanuvchisi (bo'lsa). Hisobot va "kimga ketdi"
    /// savoli uchun; yuborish uchun <see cref="RecipientAddress"/> ishlatiladi.
    /// </summary>
    public long? RecipientUserId { get; init; }

    /// <summary>
    /// Kanal ichidagi manzil — Telegram uchun <c>chat_id</c>.
    ///
    /// ★ NIMA UCHUN NUSXA (snapshot) SAQLANADI: xabar yozilgan paytdagi
    /// manzil yuborish paytidagi manzildan farq qilishi mumkin (o'quvchi
    /// botni qayta ulagan, hisob almashgan). Navbatdagi xabar YOZILGAN
    /// paytdagi qarorga sodiq qolishi kerak — aks holda "kimga ketdi"
    /// savoliga javob berib bo'lmasdi.
    /// </summary>
    public string? RecipientAddress { get; init; }

    /// <summary>Xabar turi: <c>lesson_reminder</c>, <c>recording_ready</c> kabi qisqa kod.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>
    /// YUBORISHGA TAYYOR matn. Telegram HTML qoidasiga ko'ra tayyorlangan
    /// bo'lishi shart — foydalanuvchi ma'lumoti
    /// <see cref="NotificationText.Parameter"/> orqali o'tkazilgan bo'lsin.
    /// </summary>
    public required string Body { get; init; }

    /// <summary>
    /// TAKRORLANISHGA QARSHI kalit. Bir xil kalitli ikkinchi xabar navbatga
    /// TUSHMAYDI (unikal indeks bilan mahkamlangan).
    ///
    /// KELISHUV — kalit "hodisa + obyekt + qabul qiluvchi" dan yasaladi:
    ///   <c>lesson_reminder:45:123</c>  (45-dars, 123-o'quvchi)
    /// Vaqt YOKI tasodifiy qism QO'SHILMAYDI — aks holda kalit har
    /// hisoblashda yangi bo'lib, himoya ishlamay qolardi.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// Shu vaqtdan OLDIN yuborilmasin (rejalashtirilgan eslatma uchun).
    /// Bo'sh bo'lsa — birinchi imkoniyatda yuboriladi.
    /// </summary>
    public DateTimeOffset? SendAfter { get; init; }

    /// <summary>
    /// Inline tugmalar uchun DINAMIK ma'lumot (2026-08-17, ustoz kunlik
    /// tasdiqlash). <see cref="Telegram.TelegramTemplates.EncodeButtons"/>
    /// bilan kodlangan — masalan bitta checkin'ning ID'si tugmaning
    /// <c>callback_data</c>siga kirishi kerak («Ha» va «Yo'q» har checkin
    /// uchun BOSHQA-BOSHQA tugma). <c>TemplateKey</c> orqali TANLANGAN
    /// statik tugma turlaridan (<see cref="Telegram.TelegramMarkup.RequestContact"/>
    /// kabi) farqli — bu maydon FAQAT <see cref="Telegram.TelegramMarkup.InlineButtons"/>
    /// bilan ishlaydi.
    /// </summary>
    public string? CallbackData { get; init; }
}

/// <summary>
/// Worker olib chiqqan navbat yozuvi.
///
/// Entity EMAS, NUSXA: entity <c>Infrastructure</c> da (EF), bu qatlam esa
/// uni ko'rmasligi kerak. Shu bilan birga yuborilayotgan xabarni tasodifan
/// "tahrirlab" qo'yish ham imkonsiz.
/// </summary>
public sealed record OutboxMessage(
    long Id,
    NotificationChannel Channel,
    long? RecipientUserId,
    string? RecipientAddress,
    string TemplateKey,
    string Body,
    int AttemptCount,
    string? CallbackData = null);

/// <summary>
/// Yuborish natijasi.
///
/// ★ NIMA UCHUN ISTISNO EMAS: "Telegram 400 qaytardi — chat topilmadi" bu
/// bug emas, KUTILGAN natija. Istisno bo'lganda har bunday holat Sentry'ni
/// uyg'otardi va haqiqiy xatolar shovqin ichida ko'rinmay qolardi.
/// </summary>
/// <param name="Delivered">Kanal xabarni qabul qildimi.</param>
/// <param name="Retryable">
/// Xato VAQTINCHALIKMI. <c>false</c> — qayta urinish MA'NOSIZ (masalan
/// foydalanuvchi botni bloklagan): xabar darhol <c>Failed</c> ga o'tadi va
/// navbatni bekorga band qilmaydi.
/// </param>
/// <param name="Reason">Xato tavsifi (bazaga va logga yoziladi).</param>
public sealed record MessageSendResult(bool Delivered, bool Retryable, string? Reason)
{
    /// <summary>Muvaffaqiyatli yuborildi.</summary>
    public static MessageSendResult Ok { get; } = new(Delivered: true, Retryable: false, Reason: null);

    /// <summary>Vaqtinchalik xato — backoff bilan qayta urinish kerak.</summary>
    public static MessageSendResult Retry(string reason) =>
        new(Delivered: false, Retryable: true, Reason: reason);

    /// <summary>Qaytarib bo'lmaydigan xato — qayta urinish ma'nosiz.</summary>
    public static MessageSendResult Permanent(string reason) =>
        new(Delivered: false, Retryable: false, Reason: reason);
}

/// <summary>
/// Tezlik chegarasining qarori.
/// </summary>
/// <param name="Allowed">Hozir yuborish mumkinmi.</param>
/// <param name="RetryAfter">Ruxsat bo'lmasa — qancha kutish kerak.</param>
public readonly record struct RateLimitDecision(bool Allowed, TimeSpan RetryAfter)
{
    /// <summary>Ruxsat berildi.</summary>
    public static RateLimitDecision Pass { get; } = new(Allowed: true, TimeSpan.Zero);
}

/// <summary>Bitta aylanish natijasi (log va testlar uchun).</summary>
/// <param name="Delivered">Yuborilgan xabarlar soni.</param>
/// <param name="Rejected">Xatoga uchragan (qayta urinish yoki yakuniy yiqilish) soni.</param>
/// <param name="Postponed">Tezlik chegarasi tufayli keyinga surilgan soni.</param>
public readonly record struct OutboxDispatchResult(int Delivered, int Rejected, int Postponed)
{
    /// <summary>Umuman ish bo'ldimi — worker kutish oralig'ini shunga qarab tanlaydi.</summary>
    public bool IsEmpty => Delivered == 0 && Rejected == 0 && Postponed == 0;
}
