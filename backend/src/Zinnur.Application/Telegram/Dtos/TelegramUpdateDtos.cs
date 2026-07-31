using System.Text.Json.Serialization;

namespace Zinnur.Application.Telegram.Dtos;

// ════════════════════════════════════════════════════════════════════════════
// TELEGRAM'DAN KELADIGAN JSON — DTO'lar.
//
// ★ HAR MAYDONDA [JsonPropertyName] OSHKOR YOZILGAN — bu ATAYLAB.
//
// Ilovaning global JSON sozlamasi (`Program.cs`) bizning API'imiz uchun:
// camelCase va enum'lar SATR ko'rinishida. Telegram esa O'Z qoidasi bilan
// yozadi — `snake_case` (`update_id`, `first_name`, `phone_number`).
// Global sozlamaga tayanish IKKI xil sabab bilan xavfli:
//
//   1) Sozlama bir kun o'zgarsa (yoki controller boshqa sozlamali oqimga
//      ko'chsa), maydonlar JIMGINA `null` bo'lib qolardi: webhook 200
//      qaytarardi, lekin hech narsa ishlamasdi. Bunday nosozlikni topish
//      juda qiyin — xato yo'q, natija ham yo'q.
//   2) Kelajakda `snake_case` konvensiyasi qo'shilsa, BIZNING API'imiz
//      buzilardi.
//
// Oshkor nom esa har ikki tomondan MUSTAQIL: qanday sozlama bo'lishidan
// qat'i nazar, aynan Telegram yozgan nomga bog'lanadi.
//
// ★ HAMMA MAYDON NULLABLE: Telegram yangilanish turlarini vaqti-vaqti bilan
// kengaytiradi va biz TUSHUNMAGAN yangilanish ham kelaveradi. Majburiy
// maydon bo'lsa deserializatsiya yiqilib, webhook 400 qaytarardi — Telegram
// esa 200 dan boshqa javobda AYNI yangilanishni qayta-qayta yuboraveradi.
// ════════════════════════════════════════════════════════════════════════════

/// <summary>Telegram webhook orqali yuboradigan bitta yangilanish.</summary>
public sealed record TelegramUpdateDto
{
    /// <summary>
    /// Yangilanishning O'SIB BORUVCHI raqami — TAKRORNI ANIQLASH kaliti.
    ///
    /// Telegram javobni 60 sekundda ololmasa AYNI yangilanishni qayta
    /// yuboradi. Bu raqamsiz bitta "raqamni ulashish" hodisasi ikki marta
    /// ishlanardi va o'quvchi ikkita bir xil xabar olardi.
    /// </summary>
    [JsonPropertyName("update_id")]
    public long UpdateId { get; init; }

    [JsonPropertyName("message")]
    public TelegramMessageDto? Message { get; init; }

    /// <summary>
    /// Tahrirlangan xabar. Biz uni ATAYLAB ishlatmaymiz (pastdagi izoh:
    /// <see cref="Services.TelegramUpdateHandler"/>), lekin maydon bor —
    /// shunda "nima keldi" savoliga logdan javob topiladi.
    /// </summary>
    [JsonPropertyName("edited_message")]
    public TelegramMessageDto? EditedMessage { get; init; }
}

/// <summary>Telegram xabari (matn, kontakt yoki boshqa turdagi).</summary>
public sealed record TelegramMessageDto
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; init; }

    /// <summary>
    /// Xabarni KIM yubordi. Bu YAGONA ishonchli shaxs manbai: Telegram uni
    /// o'zi to'ldiradi va foydalanuvchi o'zgartira olmaydi.
    /// </summary>
    [JsonPropertyName("from")]
    public TelegramUserDto? From { get; init; }

    [JsonPropertyName("chat")]
    public TelegramChatDto? Chat { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>
    /// Ulashilgan kontakt. ⚠️ Bu kontakt xabar yuboruvchiga TEGISHLI DEGANI
    /// EMAS — Telegram'da istalgan odamning kontaktini ilova qilib yuborish
    /// mumkin. Tekshiruv: <see cref="TelegramContactDto.UserId"/> ==
    /// <see cref="From"/>.<see cref="TelegramUserDto.Id"/>.
    /// </summary>
    [JsonPropertyName("contact")]
    public TelegramContactDto? Contact { get; init; }
}

/// <summary>Telegram foydalanuvchisi.</summary>
public sealed record TelegramUserDto
{
    /// <summary>
    /// Telegram ID. <c>long</c> — <c>int</c> EMAS: 2021 yildan beri yangi
    /// hisoblar 2^31 dan katta raqam olishi mumkin va Telegram hujjati
    /// 52 bitgacha kafolat beradi.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; init; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }
}

/// <summary>
/// Suhbat (chat). Shaxsiy suhbatda <see cref="Id"/> foydalanuvchining
/// Telegram ID'si bilan MOS KELADI — lekin biz baribir <c>chat.id</c> ni
/// ishlatamiz, chunki javob AYNAN shu suhbatga ketishi kerak.
/// </summary>
public sealed record TelegramChatDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary><c>private</c>, <c>group</c>, <c>supergroup</c>, <c>channel</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

/// <summary>Ulashilgan telefon kontakti.</summary>
public sealed record TelegramContactDto
{
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    /// <summary>
    /// Kontakt EGASINING Telegram ID'si (Telegram foydalanuvchisi bo'lsa).
    ///
    /// ★ BU MAYDON — BUTUN HIMOYANING TAYANCHI. U yuboruvchining
    /// <c>from.id</c> si bilan solishtiriladi. Mos kelmasa — foydalanuvchi
    /// BOSHQA odamning kontaktini yubormoqda va so'rov RAD ETILADI.
    /// Eski tizimda telefon umuman tekshirilmasdi (audit: X-1).
    /// </summary>
    [JsonPropertyName("user_id")]
    public long? UserId { get; init; }
}
