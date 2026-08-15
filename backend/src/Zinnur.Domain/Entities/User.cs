using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>Platforma foydalanuvchisi (o'quvchi, ustoz, kurator, o'quv bo'limi, admin).</summary>
public class User : BaseEntity
{
    public required string FullName { get; set; }

    /// <summary>Unikal. Har doim kichik harflarda saqlanadi.</summary>
    public required string Email { get; set; }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// 🔴 O'LIK USTUN — HECH KIM O'QIMAYDI (2026-08-13 dan)
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Email va parol bilan kirish BUTUNLAY olib tashlandi (loyiha
    /// egasining qarori — talab R26). Bugungi kunda bu ustunni
    /// TEKSHIRADIGAN kod YO'Q: <c>AuthService</c> da parol yo'li ham,
    /// <c>POST /api/v1/auth/login</c> ham mavjud emas.
    ///
    /// ★ NIMA UCHUN USTUN BARIBIR OLIB TASHLANMADI — ONGLI QAROR:
    ///
    ///  1) MIGRATSIYA XAVFI. Ustunni tashlash <c>NOT NULL</c> ustunni
    ///     o'chirishni, snapshot yangilashni va zanjirdagi keyingi
    ///     migratsiyalarni talab qiladi. Bu o'zgarish ALLAQACHON eng
    ///     xavfli o'zgarish (butun kirish yo'li almashdi) — unga
    ///     qaytarib bo'lmaydigan sxema o'zgarishini QO'SHISH ikki
    ///     xavfni bitta relizga bog'lab qo'yardi.
    ///
    ///  2) QAYTISH YO'LI. Telefon oqimi kutilmagan sababdan (masalan
    ///     Telegram O'zbekistonda bloklansa) ishlamay qolsa, parol yo'lini
    ///     qaytarish — bitta servisni tiklash. Ustun tashlangan bo'lsa
    ///     HAMMA parol yo'qolgan bo'lardi va har bir foydalanuvchini
    ///     qo'lda tiklash kerak bo'lardi.
    ///
    ///  3) NARXI NOL. Ustun hech qayerda o'qilmaydi; ro'yxat so'rovi uni
    ///     bazadan umuman OLMAYDI (`UserService.Projection`).
    ///
    /// ⚠️ QIYMAT ENDI MA'NOSIZ: yangi foydalanuvchilarga HECH KIMGA
    ///    ma'lum bo'lmagan tasodifiy satrning hash'i yoziladi
    ///    (<c>UserService.PlaceholderPasswordHashAsync</c>). Eski
    ///    qatorlarda haqiqiy parol hash'lari qolgan — ular ham
    ///    tekshirilmaydi.
    ///
    /// 🔴 BU USTUNNI QAYTA ISHLATMANG. "Parolni ham qo'shib qo'yaylik"
    ///    degan qadam ikkita parallel kirish yo'lini tiklaydi — bu esa
    ///    <c>IAuthService</c> izohida QAT'IY taqiqlangan. Parol qaytarilsa
    ///    u YAGONA yo'l bo'lishi yoki ikkinchi omil sifatida qurilishi
    ///    kerak, "yana bitta eshik" sifatida emas.
    /// </summary>
    public required string PasswordHash { get; set; }

    /// <summary>Foydalanuvchi kiritgan ko'rinish (bo'shliq, qavs, defis bo'lishi mumkin).</summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Qidiruv va taqqoslash uchun YAGONA ko'rinish: <c>+998901234567</c>.
    /// FILTRLI UNIKAL indeks shu ustunda — telefon bo'yicha izlash bitta
    /// indeksli <c>WHERE</c> bo'ladi.
    ///
    /// NIMA UCHUN ALOHIDA USTUN: eski tizimda <c>Phone</c> qanday kiritilgan
    /// bo'lsa shunday saqlanardi va taqqoslash uchun HAR kirishda barcha
    /// foydalanuvchilar xotiraga yuklanib, Python siklida normalizatsiya
    /// qilinardi (<c>users_svc.find_student_by_phone</c>). 100 ming yozuvda
    /// bu har so'rovda sekundlar demakdir. Endi normalizatsiya YOZUVDA bir
    /// marta bajariladi.
    ///
    /// <see cref="SetPhone"/> dan boshqa yo'l bilan o'zgartirilmaydi —
    /// shuning uchun ikki ustun bir-biriga mos kelmay qolishi mumkin emas.
    /// </summary>
    public string? PhoneNormalized { get; private set; }

    public long? TelegramId { get; set; }

    /// <summary>
    /// Telegram <c>@username</c> — <c>@</c> BELGISIZ saqlanadi.
    ///
    /// NIMA UCHUN KERAK: xodim o'quvchi bilan Telegram'da bog'lanishi kerak
    /// bo'lganda raqamli <see cref="TelegramId"/> bilan hech nima qila
    /// olmaydi — u Telegram qidiruvida ishlamaydi. Username esa bosiladigan
    /// havola (<c>t.me/...</c>).
    ///
    /// ★ SHAXSNI ANIQLAYDIGAN IDENTIFIKATOR EMAS: foydalanuvchi uni istalgan
    /// payt o'zgartiradi va bo'shatib qo'ygan nomni BOSHQA odam olib qo'yishi
    /// mumkin. Shuning uchun u FAQAT ko'rsatish uchun; shaxs har doim
    /// <see cref="TelegramId"/> bo'yicha aniqlanadi (<c>IX_Users_TelegramId</c>
    /// unikal indeksi). Bot bilan HAR muloqotda qayta yozib boriladi
    /// (<see cref="RefreshTelegramUsername"/>) — eskirgan nom xodimni boshqa
    /// odamga yo'llab qo'ymasin.
    /// </summary>
    public string? TelegramUsername { get; private set; }

    /// <summary>
    /// Telegram qachon bog'langani. <see cref="TelegramId"/> bo'lmasa DOIM
    /// <c>null</c>: ikkisi ham faqat <see cref="LinkTelegram"/> va
    /// <see cref="UnlinkTelegram"/> orqali o'zgaradi, shuning uchun
    /// "bog'lanmagan, lekin bog'lanish sanasi bor" holati mumkin emas.
    /// </summary>
    public DateTimeOffset? TelegramLinkedAt { get; private set; }

    /// <summary>
    /// PROFIL RASMI — obyekt omboridagi kalit (<c>avatars/…</c>).
    ///
    /// Loyiha egasi (2026-08-15): *"har qanday userlar o'z profiliga rasm
    /// joylash imkoniyati bo'lsin"*.
    ///
    /// ★ BAZADA FAQAT KALIT, BAYTLAR EMAS: rasm <see cref="Zinnur.Domain"/>
    /// uchun ko'rinmaydigan obyekt omborida (R2/MinIO) yotadi — vazifa
    /// biriktirmalari va dars mediasi bilan AYNI joyda. Baytlarni ustunga
    /// solish har <c>SELECT</c> ga yuzlab kilobayt qo'shardi va
    /// <c>GET /auth/me</c> (u HAR sahifada chaqiriladi) og'irlashardi.
    ///
    /// <c>null</c> — rasm yo'q, ekranda ism harfi chiziladi.
    /// </summary>
    public string? AvatarKey { get; private set; }

    /// <summary>
    /// Rasm oxirgi marta qachon almashtirilgani.
    ///
    /// ★ NIMA UCHUN KERAK — KESH BUZISH (cache-busting): rasm manzili
    /// foydalanuvchi Id'siga bog'langan (<c>…/avatar</c>), ya'ni rasm
    /// almashsa ham MANZIL O'ZGARMAYDI va brauzer eskisini ko'rsatib
    /// turardi. Bu vaqt tamg'asi manzilga so'rov parametri sifatida
    /// qo'shiladi va yangi rasm darhol ko'rinadi.
    ///
    /// <see cref="AvatarKey"/> bilan birga o'zgaradi (<see cref="SetAvatar"/>),
    /// shuning uchun "kalit bor, sanasi yo'q" holati mumkin emas.
    /// </summary>
    public DateTimeOffset? AvatarUpdatedAt { get; private set; }

    public UserRole Role { get; set; } = UserRole.Student;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Sessiyalarni bekor qilish hisoblagichi.
    /// JWT ichida `ver` claim'i sifatida yuriladi; mos kelmasa token rad etiladi.
    /// Parol almashtirilganda yoki rol o'zgarganda oshiriladi.
    ///
    /// NIMA UCHUN: eski tizimda "Chiqish" faqat cookie'ni o'chirardi va token
    /// 14 kun yaroqli qolardi — o'g'irlangan tokenni bekor qilishning iloji yo'q edi.
    /// </summary>
    public int TokenVersion { get; set; }

    /// <summary>Parol yoki rol o'zgarganda barcha mavjud tokenlarni bekor qiladi.</summary>
    public void InvalidateTokens() => TokenVersion++;

    public void ChangeRole(UserRole role)
    {
        if (Role == role) return;
        Role = role;
        InvalidateTokens();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// ⚠️ 2026-08-13 dan HECH QAYERDAN chaqirilmaydi (parol bilan kirish
    /// olib tashlandi). Metod saqlandi, chunki u <see cref="PasswordHash"/>
    /// ni <see cref="InvalidateTokens"/> bilan BIRGA o'zgartirish
    /// invariantini ushlab turadi — parol yo'li kelajakda qaytarilsa,
    /// bu bog'liqlikni qaytadan kashf qilish shart bo'lmasin.
    /// </summary>
    public void SetPassword(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash))
            throw new DomainException("Parol hash'i bo'sh bo'lishi mumkin emas.");

        PasswordHash = newHash;
        InvalidateTokens();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // ======================================================================
    // TELEGRAM BOG'LANISHI
    // ======================================================================

    /// <summary>
    /// Telegram hisobini profilga bog'laydi (bot oqimidan — telefon
    /// ulashilgandan keyin).
    /// </summary>
    public void LinkTelegram(long telegramId, string? username, DateTimeOffset now)
    {
        if (telegramId <= 0)
            throw new DomainException("Telegram ID musbat bo'lishi kerak.");

        TelegramId = telegramId;
        TelegramUsername = NormalizeTelegramUsername(username);
        TelegramLinkedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Bog'lanishni UZADI va barcha mavjud sessiyalarni bekor qiladi.
    /// Uzilgan qiymatlarni qaytaradi — chaqiruvchi ularni audit iziga yozadi.
    /// </summary>
    /// <remarks>
    /// 🔴 <see cref="InvalidateTokens"/> AYNAN SHU YERDA, chaqiruvchida EMAS.
    ///
    /// Sabab: o'quvchi platformaga FAQAT Telegram orqali kiradi (Mini App),
    /// ya'ni "bog'lanishni uzish" amalining butun ma'nosi — kirish huquqini
    /// olib qo'yish. Token versiyasi oshirilmasa o'quvchining qo'lidagi
    /// kirish tokeni yana 15 daqiqa ishlab turardi va amal JIMGINA kuchsiz
    /// bo'lib qolardi. Invariant Domain'da bo'lgani uchun yangi chaqiruv
    /// joyi qo'shilganda uni unutib bo'lmaydi.
    ///
    /// ⚠️ Bu YETARLI EMAS: sessiya holati keshi ham tozalanishi kerak
    /// (<c>IAuthStateCache</c>), aks holda token yana 60 sekund qabul
    /// qilinardi. Kesh Application qatlamida — buni chaqiruvchi bajaradi.
    /// </remarks>
    public (long TelegramId, string? Username) UnlinkTelegram(DateTimeOffset now)
    {
        if (TelegramId is not { } telegramId)
        {
            throw new DomainException(
                "Bu profilga Telegram hisobi bog'lanmagan — uzish uchun narsa yo'q.");
        }

        var username = TelegramUsername;

        TelegramId = null;
        TelegramUsername = null;
        TelegramLinkedAt = null;
        InvalidateTokens();
        UpdatedAt = now;

        return (telegramId, username);
    }

    /// <summary>
    /// Username'ni bot bilan har muloqotda yangilab boradi.
    /// Bog'lanmagan profilga TEGMAYDI.
    /// </summary>
    /// <returns>
    /// Qiymat haqiqatan o'zgardimi. <c>false</c> bo'lsa chaqiruvchi bekorga
    /// yozuv qilmaydi — bot har <c>/start</c> da bu metodni chaqiradi va
    /// aks holda har xabar <c>UPDATE</c> hosil qilardi.
    /// </returns>
    public bool RefreshTelegramUsername(string? username)
    {
        if (TelegramId is null) return false;

        var normalized = NormalizeTelegramUsername(username);

        if (string.Equals(normalized, TelegramUsername, StringComparison.Ordinal))
            return false;

        TelegramUsername = normalized;
        return true;
    }

    /// <summary>
    /// <c>@</c> ni olib tashlaydi, bo'sh qiymatni <c>null</c> ga aylantiradi
    /// va uzunlikni chegaraga QIRQADI (istisno KO'TARMAYDI).
    ///
    /// Nima uchun qirqiladi: Telegram username'ni 32 belgi bilan kafolatlaydi,
    /// lekin bu qiymat TASHQI tizimdan keladi. Kutilmagan uzun qiymat istisno
    /// ko'tarsa butun webhook yiqilardi va o'quvchi bog'lanish o'rniga
    /// jimgina xato olardi — ko'rsatish uchun ishlatiladigan maydon buni
    /// oqlamaydi.
    /// </summary>
    private static string? NormalizeTelegramUsername(string? username)
    {
        var value = username?.Trim().TrimStart('@').Trim();

        if (string.IsNullOrEmpty(value)) return null;

        return value.Length <= MaxTelegramUsernameLength
            ? value
            : value[..MaxTelegramUsernameLength];
    }

    /// <summary>Telegram'ning o'z chegarasi — 32 belgi.</summary>
    public const int MaxTelegramUsernameLength = 32;

    /// <summary>
    /// Profil rasmini o'rnatadi yoki olib tashlaydi (<paramref name="objectKey"/>
    /// <c>null</c> bo'lsa).
    /// </summary>
    /// <returns>
    /// ESKI kalit (yoki <c>null</c>). Chaqiruvchi uni OMBORDAN o'chirishi
    /// kerak.
    ///
    /// ★ NIMA UCHUN QAYTARILADI, nega domen o'zi o'chirmaydi: domen
    /// qatlami omborni KO'RMAYDI (u Application portida). Eski kalitni
    /// jimgina tashlab yuborsak, ombor har almashtirishda "yetim" fayl
    /// bilan to'lib borardi — foydalanuvchi rasmini kuniga bir marta
    /// almashtirsa yiliga 365 ta ortiqcha obyekt.
    /// </returns>
    public string? SetAvatar(string? objectKey, DateTimeOffset now)
    {
        var previous = AvatarKey;

        var value = string.IsNullOrWhiteSpace(objectKey) ? null : objectKey.Trim();

        AvatarKey = value;

        // Sana KALIT BILAN BIRGA o'zgaradi: rasm o'chirilganda ham
        // yangilanadi, aks holda brauzer keshidagi eski rasm "o'chirilgan"
        // holatda ham ko'rinib turardi.
        AvatarUpdatedAt = value is null && previous is null ? AvatarUpdatedAt : now;

        // O'ZI O'ZIGA almashtirilgan bo'lsa (nazariy holat) eski kalit
        // QAYTARILMAYDI — aks holda hozirgina yozilgan fayl o'chirilardi.
        return previous == value ? null : previous;
    }

    /// <summary>
    /// Telefonni o'rnatadi va <see cref="PhoneNormalized"/> ni AVTOMATIK hisoblaydi.
    /// Telefonni o'zgartirishning yagona yo'li — normalizatsiyani unutib bo'lmaydi.
    /// </summary>
    public void SetPhone(string? rawPhone)
    {
        var normalized = NormalizePhone(rawPhone);

        // Raqamsiz matn ("-", "yo'q") telefonsiz deb qaraladi.
        Phone = normalized is null ? null : rawPhone?.Trim();
        PhoneNormalized = normalized;
    }

    /// <summary>
    /// Telefonni taqqoslash uchun bir ko'rinishga keltiradi: faqat raqamlar + <c>+</c>.
    /// O'zbekiston uchun 9 xonali lokal raqam (<c>901234567</c>) ga <c>998</c>
    /// prefiksi qo'shiladi, <c>0998...</c> ko'rinishidagi boshdagi nol olib tashlanadi.
    /// Raqam topilmasa <c>null</c> qaytaradi.
    /// </summary>
    public static string? NormalizePhone(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone)) return null;

        Span<char> digits = stackalloc char[MaxPhoneDigits];
        var length = 0;

        foreach (var ch in rawPhone)
        {
            if (!char.IsAsciiDigit(ch)) continue;
            if (length == MaxPhoneDigits) return null;      // haddan tashqari uzun -> yaroqsiz
            digits[length++] = ch;
        }

        if (length == 0) return null;

        var value = new string(digits[..length]);

        return length switch
        {
            9 => "+998" + value,                            // 901234567    -> +998901234567
            13 when value[0] == '0' => "+" + value[1..],     // 0998901234567 -> +998901234567
            _ => "+" + value,
        };
    }

    /// <summary>E.164 chegarasi 15 raqam; zaxira bilan 18.</summary>
    private const int MaxPhoneDigits = 18;
}
