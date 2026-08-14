using System.Collections.ObjectModel;
using System.Globalization;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Settings;

/// <summary>
/// ========================================================================
/// SOZLAMALAR REGISTRI — platformadagi BARCHA sozlamalarning yagona ro'yxati
/// ========================================================================
///
/// ★ NIMA UCHUN KODDA: registr bazada bo'lganda yangi kalit SQL bilan
/// qo'shilardi va hech qanday ko'rikdan o'tmasdi. Kodda bo'lgani uchun:
/// noma'lum kalit umuman mavjud emas (404), har kalitning turi va chegarasi
/// bor, va "bu kalitni kim o'qiydi?" degan savolga javob qidiruv bilan
/// topiladi.
///
/// ★ ENV vs BAZA — ASOSIY QOIDA:
///   1) baza qatori bor      -> O'SHA (panelda o'zgartirilgan)
///   2) yo'q, env/appsettings-> O'SHA (deploy bilan kelgan boshlang'ich qiymat)
///   3) u ham yo'q           -> registrdagi standart
/// Ya'ni ENV — BOSHLANG'ICH, BAZA — USTUN.
///
/// ★ ISTISNO — <see cref="SettingSource.Environment"/> kalitlari:
/// ular uchun baza UMUMAN O'QILMAYDI. Bu ro'yxatga tushish mezoni bitta:
/// "kalit bazadan boshqarilsa tizim o'zini o'zi qulflab qo'yadimi yoki
/// xavfsizlik buziladimi?". Har biri uchun sabab
/// <c>ReadOnlyReason</c> da yozilgan va panelda AYNAN shu matn ko'rinadi.
///
/// ★ SIRLAR BAZADA OCHIQ SAQLANADI (shifrlanmaydi). Bu ONGLI QAROR, sabab
/// <see cref="Zinnur.Application.Settings.ISettingsStore"/> izohida batafsil.
/// Qisqasi: shifrlash kalitni talab qiladi, kalit esa muhitda bo'lardi —
/// ya'ni bazani VA muhitni ko'ra olgan hujumchiga foydasi yo'q, lekin
/// kalitni yo'qotish butun integratsiyani qaytarib bo'lmaydigan qiladi.
/// Aynan shuning uchun ro'yxatga tanlov QAT'IY: bazaga faqat AYLANTIRIB
/// (rotate) qutulsa bo'ladigan sirlar tushadi. Tizimni QULFLAY yoki huquqni
/// KENGAYTIRA oladigan sirlar (JWT kaliti, baza ulanish satri) bazaga
/// UMUMAN tushmaydi: bazani o'qiy olgan odam token qalbakilashtira
/// olmasligi kerak.
///
/// ★ <see cref="SettingSource.Database"/> KALITLARI ISH JARAYONIDA
/// O'QILADI. Ular <c>IOptions&lt;T&gt;</c> ga QOTIB QOLMAYDI:
/// <c>IRuntimeSettings</c> keshi orqali har chaqiruvda yangi qiymat
/// olinadi (kesh: shu instansiyada darhol, boshqalarida eng ko'pi 10 s).
/// Ya'ni panel "saqlandi" desa — tizim HAQIQATAN yangi qiymat bilan
/// ishlaydi.
/// </summary>
public static class SettingsRegistry
{
    /// <summary>Barcha sozlamalar — e'lon tartibida (panel shu tartibda chizadi).</summary>
    public static IReadOnlyList<SettingDefinition> All { get; } = Build();

    private static readonly ReadOnlyDictionary<string, SettingDefinition> Index =
        new(All.ToDictionary(d => d.Key, StringComparer.Ordinal));

    /// <summary>Kalit bo'yicha topadi. Noma'lum kalit — <c>false</c> (404 ga aylanadi).</summary>
    public static bool TryGet(string? key, out SettingDefinition definition)
    {
        definition = null!;

        if (string.IsNullOrWhiteSpace(key))
            return false;

        return Index.TryGetValue(key, out definition!);
    }

    /// <summary>Guruh nomi (o'zbekcha) — panel bo'lim sarlavhasi sifatida ishlatadi.</summary>
    public static string GroupName(SettingGroup group) => group switch
    {
        SettingGroup.General => "Umumiy",
        SettingGroup.Finance => "Moliya",
        SettingGroup.Telegram => "Telegram",
        SettingGroup.LiveKit => "LiveKit (jonli dars)",
        SettingGroup.Storage => "Ombor (fayllar)",
        SettingGroup.Security => "Xavfsizlik",
        SettingGroup.Content => "O'quv kontenti",
        SettingGroup.Chat => "Guruh chati",
        _ => group.ToString(),
    };

    /// <summary>Guruh izohi — bo'lim ostidagi bir qatorli tushuntirish.</summary>
    public static string GroupDescription(SettingGroup group) => group switch
    {
        SettingGroup.General => "Platformaning umumiy xatti-harakati.",
        SettingGroup.Finance => "Qarz uchun bloklash chegarasi va qamrovi.",
        SettingGroup.Telegram => "Telegram bot orqali xabar yuborish.",
        SettingGroup.LiveKit => "Jonli dars serveri bilan bog'lanish parametrlari.",
        SettingGroup.Storage => "Uy vazifasi fayllari saqlanadigan obyekt ombori.",
        SettingGroup.Security => "Sessiya, token va ulanish sirlari.",
        SettingGroup.Content => "Dars videosi va rasmlari uchun hajm chegaralari.",
        SettingGroup.Chat =>
            "Guruh chati tarixini avtomatik tozalash. 🔴 O'chirish QAYTARILMAYDI.",
        _ => string.Empty,
    };

    /// <summary>
    /// ⚠️ Bu matn BIR NECHTA kalitda takrorlanadi, chunki sabab ham AYNI:
    /// qiymat ilova ISHGA TUSHGANDA bir marta o'qiladi va singleton xizmatga
    /// qotib qoladi. Bazadan o'zgartirilsa panel "saqlandi" derdi-yu, tizim
    /// eski qiymat bilan ishlayverardi — bu eng yomon turdagi xato:
    /// jimgina yolg'on.
    /// </summary>
    private const string StartupBoundReason =
        "Bu qiymat ilova ishga tushganda bir marta o'qiladi va xizmatlarga qotib qoladi. "
        + "Paneldan o'zgartirilsa tizim baribir eski qiymat bilan ishlayverardi. "
        + "O'zgartirish uchun muhit o'zgaruvchisini tahrirlab, API'ni qayta ishga tushiring.";

    private static SettingDefinition[] Build() =>
    [
        // ================================================================ UMUMIY

        new()
        {
            Key = "general.time_zone",
            Group = SettingGroup.General,
            DisplayName = "Vaqt zonasi",
            Description =
                "Dars jadvali AYNAN shu zonaning devor-vaqtida tuziladi. "
                + "Guruhning 19:00 boshlanish vaqti shu zona bo'yicha UTC'ga o'giriladi.",
            Kind = SettingValueKind.Text,
            Format = SettingFormat.TimeZone,
            Source = SettingSource.Environment,
            ConfigurationKey = "App:TimeZone",
            DefaultValue = "Asia/Tashkent",
            ReadOnlyReason =
                "Zona o'zgarsa ALLAQACHON yaratilgan darslar eski zonada, yangilari esa "
                + "yangisida hisoblanardi — jadval ikkiga bo'linib ketardi. Bundan tashqari "
                + "zona ishga tushishda bir marta o'qiladi. " + StartupBoundReason,
        },

        // ================================================================ MOLIYA

        new()
        {
            Key = "finance.block_threshold",
            StorageKey = FinanceKeys.Threshold,
            Group = SettingGroup.Finance,
            DisplayName = "Bloklash chegarasi (so'm)",
            Description =
                "Qarz SHU SUMMADAN OSHGANDA o'quvchi bloklanadi (teng bo'lsa bloklanmaydi). "
                + "Tariflar ko'tarilganda o'quv bo'limi shu raqamni o'zgartiradi — "
                + "shuning uchun u bazada va o'zgarish darhol kuchga kiradi.",
            Kind = SettingValueKind.Money,
            Source = SettingSource.Database,
            ConfigurationKey = "Payments:DefaultBlockThreshold",
            DefaultValue = "540000",
            Minimum = 0m,

            // Yuqori chegara ATAYLAB bor: nol qo'shib yuborish (5 400 000)
            // hech kimni bloklamaydigan holatga olib kelardi va buni hech kim
            // sezmasdi. 100 mln — aniq xato belgisi.
            Maximum = 100_000_000m,
        },

        new()
        {
            Key = "finance.block_scope",
            StorageKey = FinanceKeys.Scope,
            Group = SettingGroup.Finance,
            DisplayName = "Bloklash qamrovi",
            Description =
                "Qarzdor nimadan mahrum bo'ladi: None — hech nimadan, Video — video darslardan, "
                + "Live — jonli darsdan, Platform — butun platformadan.",
            Kind = SettingValueKind.Choice,
            Source = SettingSource.Database,
            ConfigurationKey = "Payments:DefaultBlockScope",
            DefaultValue = nameof(PaymentBlockScope.Video),
            Choices = Enum.GetNames<PaymentBlockScope>(),
        },

        new()
        {
            Key = "finance.enforce_block",
            Group = SettingGroup.Finance,
            DisplayName = "Qattiq rejim",
            Description =
                "Yoqilgan bo'lsa qarzdor haqiqatan bloklanadi. O'chirilgan bo'lsa qarz "
                + "hisoblanadi va ko'rinadi, lekin hech kim bloklanmaydi (yumshoq rejim).",
            Kind = SettingValueKind.Toggle,
            Source = SettingSource.Environment,
            ConfigurationKey = "Payments:EnforceBlock",
            DefaultValue = "true",
            ReadOnlyReason =
                "Bu MUHIT xossasi, biznes qarori emas. Staging bazasi odatda prod nusxasidan "
                + "tiklanadi — kalit bazada tursa prod'ning \"qattiq rejim\" qiymati staging'ga "
                + "ham ko'chib o'tardi va sinov foydalanuvchilari bloklanib qolardi. "
                + "Muhit o'zgaruvchisi esa nusxa bilan ko'chmaydi.",
        },

        // ================================================================ TELEGRAM

        new()
        {
            Key = "telegram.bot_token",
            Group = SettingGroup.Telegram,
            DisplayName = "Bot tokeni",
            Description =
                "@BotFather bergan token. Xabar yuborish va Mini App imzosini tekshirish "
                + "uchun kerak. Bo'sh bo'lsa Telegram funksiyalari butunlay o'chiq bo'ladi. "
                + "★ Token o'g'irlanganda uni SHU YERDAN almashtirish kifoya — serverga "
                + "kirish yoki qayta joylashtirish shart emas. "
                + "🔴 2026-08-13 dan bu qiymat TIZIMGA KIRISHNI ham belgilaydi: "
                + "email va parol olib tashlangan, kirishning ikkala yo'li ham "
                + "(Mini App imzosi va telefonga yuboriladigan kod) shu tokenga tayanadi.",
            Kind = SettingValueKind.Secret,
            Format = SettingFormat.TelegramToken,
            Source = SettingSource.Database,
            ConfigurationKey = "Telegram:BotToken",
            MaxLength = 200,

            // 🔴 O'LIK HALQANI UZADIGAN KALIT — sabab
            //    `SettingDefinition.OverrideConfigurationKey` izohida.
            OverrideConfigurationKey = TelegramOverrideKeys.BotToken,
            OverrideReason = TelegramOverrideKeys.Reason,
        },

        new()
        {
            Key = "telegram.webhook_secret",
            Group = SettingGroup.Telegram,
            DisplayName = "Webhook siri",
            Description =
                "Telegram har yangilanishda `X-Telegram-Bot-Api-Secret-Token` sarlavhasida "
                + "qaytaradigan sir. Usiz webhook endpointi umuman ishlamaydi. "
                + "⚠️ O'zgartirgandan keyin Telegram tomonida `setWebhook` ni AYNI sir bilan "
                + "qayta chaqiring, aks holda bot yangilanishlarni qabul qilmay qo'yadi.",
            Kind = SettingValueKind.Secret,
            Format = SettingFormat.TelegramSecret,
            Source = SettingSource.Database,
            ConfigurationKey = "Telegram:WebhookSecret",
            MaxLength = 256,

            // 🔴 TOKEN BILAN JUFTLIKDA. `TelegramOptions.IsConfigured`
            //    IKKALASINI ham talab qiladi — ya'ni faqat tokenni
            //    ustidan yozish yetarli bo'lmasdi: sir buzuq qolsa
            //    integratsiya baribir "sozlanmagan" holatida turardi va
            //    tiklash yo'li yarim ochiq bo'lardi.
            OverrideConfigurationKey = TelegramOverrideKeys.WebhookSecret,
            OverrideReason = TelegramOverrideKeys.Reason,
        },

        new()
        {
            Key = "telegram.mini_app_url",
            Group = SettingGroup.Telegram,
            DisplayName = "Mini App manzili",
            Description =
                "Botdagi \"Ilovani ochish\" tugmasi shu manzilga olib boradi. "
                + "Telegram `web_app` tugmasi uchun faqat `https://` qabul qiladi — "
                + "`http://` bo'lsa Bot API BUTUN xabarni 400 bilan rad etadi.",
            Kind = SettingValueKind.Text,
            Format = SettingFormat.Url,
            Source = SettingSource.Database,
            ConfigurationKey = "Telegram:MiniAppUrl",
        },

        new()
        {
            Key = "telegram.bot_username",
            Group = SettingGroup.Telegram,
            DisplayName = "Bot foydalanuvchi nomi",
            Description = "`@` siz yoziladi. Frontend \"botni ochish\" havolasini shundan yasaydi.",
            Kind = SettingValueKind.Text,
            Source = SettingSource.Database,
            ConfigurationKey = "Telegram:BotUsername",
            MaxLength = 100,
        },

        new()
        {
            Key = "telegram.api_base_url",
            Group = SettingGroup.Telegram,
            DisplayName = "Bot API manzili",
            Description =
                "Odatda `https://api.telegram.org`. Katta yuklamada lokal "
                + "`telegram-bot-api` serveriga yo'naltiriladi.",
            Kind = SettingValueKind.Text,
            Format = SettingFormat.Url,
            Source = SettingSource.Environment,
            ConfigurationKey = "Telegram:ApiBaseUrl",
            DefaultValue = "https://api.telegram.org",
            ReadOnlyReason =
                "🔴 XAVFSIZLIK. Bot tokeni so'rov MANZILINING ICHIDA yuboriladi "
                + "(`/bot<token>/sendMessage`). Manzil paneldan boshqarilsa, panelga kirgan "
                + "odam uni o'z serveriga yo'naltirib, BIRINCHI xabar bilan birga TOKENNI "
                + "qo'lga kiritardi — ya'ni tokenni panelda almashtirish himoyasi ham "
                + "ma'nosiz bo'lardi. Manzil serverda, muhit o'zgaruvchisida qoladi.",
        },

        // ================================================================ LIVEKIT

        new()
        {
            Key = "livekit.url",
            Group = SettingGroup.LiveKit,
            DisplayName = "Ichki manzil",
            Description = "Backend LiveKit serveriga ulanadigan manzil (Docker tarmog'i ichida).",
            Kind = SettingValueKind.Text,
            Format = SettingFormat.Url,
            Source = SettingSource.Environment,
            ConfigurationKey = "LiveKit:Url",
            ReadOnlyReason =
                "Manzil tarmoq TOPOLOGIYASI — u konteynerlar joylashuvi bilan birga o'zgaradi, "
                + "panel orqali emas. Bundan tashqari AYNI qiymatni sog'liq tekshiruvi "
                + "(`/health/ready`) to'g'ridan-to'g'ri konfiguratsiyadan o'qiydi: bazadan "
                + "boshqarilsa, probe bir manzilni, token esa boshqasini ko'rsatib, "
                + "\"sog'lom, lekin dars ochilmaydi\" degan chalg'ituvchi holat paydo bo'lardi. "
                + StartupBoundReason,
        },

        new()
        {
            Key = "livekit.public_url",
            Group = SettingGroup.LiveKit,
            DisplayName = "Brauzer uchun manzil",
            Description =
                "O'quvchi brauzeri ulanadigan manzil. HTTPS sahifadan `ws://` ga ulanishni "
                + "brauzer bloklaydi — prod'da `wss://` bo'lishi shart.",
            Kind = SettingValueKind.Text,
            Format = SettingFormat.Url,
            Source = SettingSource.Environment,
            ConfigurationKey = "LiveKit:PublicUrl",
            ReadOnlyReason =
                "Manzil sertifikat va DNS bilan birga o'zgaradi — ya'ni u DEPLOY qarori, "
                + "biznes qarori emas. Xato qiymat butun jonli darsni to'xtatardi va buni "
                + "faqat dars boshlanganda bilardik. " + StartupBoundReason,
        },

        new()
        {
            Key = "livekit.api_key",
            Group = SettingGroup.LiveKit,
            DisplayName = "API kalit nomi",
            Description =
                "LiveKit serveridagi `LIVEKIT_KEYS` dagi kalit nomi (masalan `devkey`). "
                + "⚠️ Kalit nomi va siri LiveKit serveridagi qiymat bilan JUFTLIKDA ishlaydi: "
                + "ikkalasini AYNI paytda, server sozlamasi bilan birga almashtiring.",
            Kind = SettingValueKind.Text,
            Source = SettingSource.Database,
            ConfigurationKey = "LiveKit:ApiKey",

            // Bo'sh kalit nomi tokenning `iss` claim'ini bo'shatib qo'yardi va
            // LiveKit tokenni XATO BERMASDAN rad etardi — o'quvchi shunchaki
            // "ulanmadi" holatida qolardi. `ValidateOnStart` dagi
            // `[Required]` ning yozish paytidagi ekvivalenti.
            MinLength = 1,
            MaxLength = 100,
        },

        new()
        {
            Key = "livekit.api_secret",
            Group = SettingGroup.LiveKit,
            DisplayName = "API siri",
            Description =
                "Jonli dars tokenlarini imzolash kaliti (HS256, kamida 32 belgi). "
                + "⚠️ LiveKit serveridagi qiymat bilan AYNAN bir xil bo'lishi shart — "
                + "mos kelmasa hamma jonli dars bir zumda uziladi.",
            Kind = SettingValueKind.Secret,
            Source = SettingSource.Database,
            ConfigurationKey = "LiveKit:ApiSecret",

            // `LiveKitOptions.MinSecretLength` bilan AYNI raqam: ilgari uni
            // `ValidateOnStart` qo'riqlardi, endi paneldan ham qisqa kalit
            // yozib bo'lmasin.
            MinLength = 32,
        },

        // ================================================================ OMBOR

        new()
        {
            Key = "storage.service_url",
            Group = SettingGroup.Storage,
            DisplayName = "Ombor manzili",
            Description =
                "S3 mos xizmat manzili (Cloudflare R2 yoki MinIO). "
                + "⚠️ Manzil bilan REGION birga o'zgaradi: MinIO odatda `us-east-1`, "
                + "R2 esa `auto`. Mos kelmasa ombor 403 `SignatureDoesNotMatch` beradi.",
            Kind = SettingValueKind.Text,
            Format = SettingFormat.Url,
            Source = SettingSource.Database,
            ConfigurationKey = "Storage:ServiceUrl",

            // Bo'shatish faqat "standart qiymatga qaytarish" orqali — aks holda
            // ishlab turgan omborni bitta tasodifiy bo'sh saqlash bilan
            // o'chirib qo'yish mumkin bo'lardi (izoh: `SettingCoupling`).
            MinLength = 1,
        },

        new()
        {
            Key = "storage.public_url",
            Group = SettingGroup.Storage,
            DisplayName = "Brauzer uchun ombor manzili",
            Description =
                "Dars yozuvining imzolangan ko'rish havolasi SHU manzildan quriladi. "
                + "Bo'sh qoldirilsa yuqoridagi «Ombor manzili» ishlatiladi. "
                + "⚠️ Dev'da ular FARQ QILADI: backend omborga Docker tarmog'i ichidan "
                + "(`http://minio:9000`) boradi, brauzer esa faqat host portini "
                + "(`http://localhost:9010`) ko'radi. "

                // ═══════════════════════════════════════════════════════
                // 🔴 ENG QIMMAT TUZOQ — U AYNAN SHU MAYDONDA YASHIRINGAN
                //
                // Xato qiymat 500 ham, 400 ham bermaydi: brauzer omborga
                // BORADI va 403 oladi. Ya'ni sozlama xatosi "video
                // ochilmayapti" ko'rinishida chiqadi va uni Egress,
                // kalitlar yoki tarmoqdan qidirish bilan kunlar
                // yo'qotiladi. Ogohlantirish PANELDA turishi shart —
                // qidiruv aynan o'sha yerdan boshlanadi.
                // ═══════════════════════════════════════════════════════
                + "🔴 XATO MANZIL «JIM 403» BERADI. S3 imzosi (SigV4) URL'ning "
                + "HOST qismini ham imzolaydi: bu yerdagi manzil ombor haqiqatan "
                + "javob beradigan manzil bilan mos kelmasa, havola TO'G'RI "
                + "imzolangan bo'lsa-da ombor uni rad etadi. Xato serverning "
                + "logida KO'RINMAYDI (so'rov bizga umuman kelmaydi) — "
                + "foydalanuvchi faqat «video ochilmadi» deb ko'radi. "
                + "R2 uchun bu odatda `https://<hisob>.r2.cloudflarestorage.com` "
                + "yoki bucket'ga ulangan o'z domeningiz.",
            Kind = SettingValueKind.Text,
            Format = SettingFormat.Url,

            // ═══════════════════════════════════════════════════════════
            // ★★ 2026-08-13: `Environment` DAN `Database` GA O'TKAZILDI.
            //
            // Loyiha egasining talabi: *"cloudflareni ham ulanish
            // joylarini admin panel orqali boshqaradigan qilib ket"*.
            // Cloudflare R2 uchun AYNAN shu maydon — yozuvlarni KO'RISH
            // uchun ulanish nuqtasi.
            //
            // ── ESKI SABAB VA UNGA JAVOB (o'chirilmaydi) ──────────────
            //
            // Eski `ReadOnlyReason` ikki dalilga tayanardi:
            //
            //  1) "Bu DEPLOY qarori: DNS va sertifikat bilan birga
            //     o'zgaradi." — ⚠️ TO'G'RI, LEKIN AYNAN SHU SABABDAN
            //     PANELDA BO'LISHI KERAK. Domen o'zgarganda bugungi
            //     yagona yo'l — `.env` ni tahrirlab QAYTA JOYLASHTIRISH.
            //     Ya'ni eng shoshilinch daqiqada eng sekin yo'l; ayni
            //     mulohaza `storage.access_key` uchun allaqachon qabul
            //     qilingan edi (u ham paneldan aylantiriladi).
            //
            //  2) 🔴 "Panelga kirgan odam yozuvlar oqimini o'z serveriga
            //     burib yubora olardi." — ⚠️ BU DALIL IZCHIL EMAS EDI.
            //     Panelga kirgan odam ALLAQACHON `storage.service_url`,
            //     `storage.bucket`, `storage.access_key` va
            //     `storage.secret_key` ni boshqaradi (hammasi
            //     `Database`), ya'ni yozuvlarni O'Z omboriga YOZDIRA
            //     oladi. Faqat KO'RISH manzilini yopib qo'yish hech
            //     qanday xavfsizlik bermasdi — u faqat qayta
            //     joylashtirish majburiyatini qo'shardi. Bu yerdagi
            //     haqiqiy chegara — `Admin` roli va `AppSettings` auditi,
            //     `Source` emas.
            //
            // ★ `livekit.public_url` DAN FARQI SHUNDA: u brauzerning
            //   WebSocket ulanishi (jonli dars) va u sog'liq tekshiruvi
            //   bilan juftlashgan (`RuntimeLiveKitOptions` izohi). Bu
            //   maydon esa faqat imzolangan havolaning host qismi —
            //   hech qanday probe unga qaramaydi.
            // ═══════════════════════════════════════════════════════════
            Source = SettingSource.Database,
            ConfigurationKey = "Storage:PublicUrl",

            // MinLength YO'Q: bo'sh qiymat MA'NOLI — u "«Ombor manzili»
            // ishlatilsin" degani (`RuntimeStorageOptions.Compose`). Boshqa
            // `Storage:*` kalitlaridan farqi shu, shuning uchun u
            // `SettingCoupling` ning majburiy to'plamiga ham kirmaydi.
        },

        new()
        {
            Key = "storage.bucket",
            Group = SettingGroup.Storage,
            DisplayName = "Bucket nomi",
            Description =
                "Fayllar yoziladigan bucket. ⚠️ O'zgartirilsa ALLAQACHON yuklangan fayllar "
                + "topilmay qoladi (ular eski bucket'da qolgan) — o'quvchining topshirgan ishi "
                + "yo'qolgandek ko'rinadi. Faqat ombor ko'chirilganda o'zgartiring.",
            Kind = SettingValueKind.Text,
            Source = SettingSource.Database,
            ConfigurationKey = "Storage:Bucket",
            MinLength = 1,
            MaxLength = 100,
        },

        new()
        {
            Key = "storage.access_key",
            Group = SettingGroup.Storage,
            DisplayName = "Kirish kaliti",
            Description =
                "Ombor uchun `AccessKey` (S3 imzosining ochiq qismi). "
                + "★ R2 kalitlari aylantirilganda (rotate) SHU YERDAN almashtiriladi — "
                + "qayta joylashtirish kutish shart emas.",
            Kind = SettingValueKind.Secret,
            Source = SettingSource.Database,
            ConfigurationKey = "Storage:AccessKey",
        },

        new()
        {
            Key = "storage.secret_key",
            Group = SettingGroup.Storage,
            DisplayName = "Maxfiy kalit",
            Description =
                "Ombor uchun `SecretKey`. S3 imzosi shu bilan hisoblanadi. "
                + "⚠️ Xato qiymatda ombor 403 `SignatureDoesNotMatch` qaytaradi va fayl "
                + "yuklash 503 bo'lib qoladi — kalitni juftligi bilan birga almashtiring.",
            Kind = SettingValueKind.Secret,
            Source = SettingSource.Database,
            ConfigurationKey = "Storage:SecretKey",
        },

        new()
        {
            Key = "storage.region",
            Group = SettingGroup.Storage,
            DisplayName = "Region",
            Description =
                "R2 uchun `auto`, AWS S3 uchun haqiqiy region (`eu-central-1`), "
                + "MinIO uchun odatda `us-east-1`. Qiymat S3 IMZOSIGA kiradi.",
            Kind = SettingValueKind.Text,
            Source = SettingSource.Database,
            ConfigurationKey = "Storage:Region",
            DefaultValue = "auto",

            // Bo'sh region imzo zanjirini buzadi va har so'rov 403 bilan
            // qaytardi — sababi esa hech qayerda ko'rinmasdi.
            MinLength = 1,
            MaxLength = 50,
        },

        new()
        {
            Key = "storage.key_prefix",
            Group = SettingGroup.Storage,
            DisplayName = "Kalit prefiksi",
            Description = "Bitta bucket ichida modullarni ajratish uchun papka nomi.",
            Kind = SettingValueKind.Text,
            Source = SettingSource.Environment,
            ConfigurationKey = "Storage:KeyPrefix",
            DefaultValue = "submissions",
            MaxLength = 100,
            ReadOnlyReason =
                "Prefiks — ombor ICHIDAGI joylashuv sxemasi, kirish ma'lumoti emas: uni "
                + "aylantirish kerak bo'lmaydi, o'zgartirilsa esa ALLAQACHON yuklangan "
                + "fayllarga yo'l uziladi (ular eski prefiks bilan yozilgan). Boshqa "
                + "`Storage:*` kalitlaridan farqli o'laroq bu qiymat ishga tushishda "
                + "o'qiladi. " + StartupBoundReason,
        },

        // ================================================================ O'QUV KONTENTI

        new()
        {
            Key = "lesson.video_max_mb",
            Group = SettingGroup.Content,
            DisplayName = "Dars videosi hajmi (MB)",
            Description =
                "Bitta video QISMI shu hajmdan oshmasligi kerak. Dars bir necha qismga "
                + "bo'linadi, ya'ni chegara BUTUN darsga emas, har faylga tegishli. "
                + "Oshsa server 413 qaytaradi. "
                + "⚠️ Qiymatni oshirishdan oldin nginx'dagi `client_max_body_size` ni "
                + "tekshiring — u kichik bo'lsa so'rov API'ga umuman yetib bormaydi.",
            Kind = SettingValueKind.Number,
            Source = SettingSource.Database,
            ConfigurationKey = "Lessons:VideoMaxMb",
            DefaultValue = "1024",

            // Pastki chegara 1 MB: nol yoki manfiy qiymat yuklashni butunlay
            // o'chirib qo'yardi va sabab UI'da "413" dan boshqa hech narsa
            // bo'lmasdi.
            Minimum = 1m,

            // ★ YUQORI CHEGARA — SHUNCHAKI "aql bovar qilmaydigan qiymat"
            //   FILTRI EMAS. So'rov tanasining QAT'IY yuqori chegarasi
            //   (`LessonAssetsController.MaxUploadBytes`) AYNAN shu raqamdan
            //   kelib chiqadi: ASP.NET multipart faylni MODEL BOG'LASHDA,
            //   ya'ni bizning kodimizdan OLDIN vaqtinchalik diskka
            //   buferlaydi. Ya'ni administrator qo'yadigan eng katta qiymat
            //   = eng yomon holatdagi disk sarfi. 2048 MB — bir dars qismi
            //   uchun yetarlicha ko'p va disk uchun hamon xavfsiz.
            Maximum = 2048m,
        },

        new()
        {
            Key = "lesson.image_max_mb",
            Group = SettingGroup.Content,
            DisplayName = "Imtihon rasmi hajmi (MB)",
            Description =
                "Imtihon darsiga yuklanadigan bitta rasm shu hajmdan oshmasligi kerak. "
                + "Skaner qilingan A4 varaq odatda 1-3 MB. Oshsa server 413 qaytaradi.",
            Kind = SettingValueKind.Number,
            Source = SettingSource.Database,
            ConfigurationKey = "Lessons:ImageMaxMb",
            DefaultValue = "10",
            Minimum = 1m,

            // 100 MB — rasm uchun aniq xato belgisi (bunday "rasm" amalda
            // yo'q). Chegarani ochiq qoldirish imtihon rasmi yo'lini video
            // yuklash yo'liga aylantirib qo'yardi.
            Maximum = 100m,
        },

        // ================================================================ DARS YOZUVLARI (R5)

        new()
        {
            Key = "recordings.visible_to_students",
            Group = SettingGroup.Content,
            DisplayName = "Dars yozuvlari o'quvchilarga ochiq",
            Description =
                "O'chirilsa \"Dars yozuvlari\" BO'LIMI o'quvchilar uchun BUTUNLAY yopiladi: "
                + "ro'yxat bo'shaydi, \"O'quv\" ekranidagi kirish kartochkasi yo'qoladi va "
                + "eski havola bilan ochishga urinish ham rad etiladi. "
                + "⚠️ Yozib olishning O'ZIGA TEGMAYDI — darslar baribir yoziladi va o'quv "
                + "bo'limi ularni ko'raveradi; faqat o'quvchiga ko'rsatilmaydi. "
                + "★ Bu — ENG QO'POL kalit. Bitta guruhni yopish uchun guruh tahririda "
                + "\"Yozuvlar o'quvchilarga ochiq\", bitta darsni yopish uchun esa "
                + "yozuvlar ro'yxatidagi tugma ishlatiladi.",
            Kind = SettingValueKind.Toggle,
            Source = SettingSource.Database,
            DefaultValue = "true",
        },

        // ================================================================ GURUH CHATI
        //
        // 🔴 BU IKKI KALIT MA'LUMOTNI DOIMIY O'CHIRADI. Registrdagi boshqa
        // hech bir kalit bunday emas — shuning uchun ular alohida bo'limda
        // va ikkalasining izohi ham "nima yo'qoladi" degan savoldan
        // boshlanadi. Mexanizm izohi: `ChatRetentionJob`.

        new()
        {
            Key = "chat.retention_enabled",
            Group = SettingGroup.Chat,
            DisplayName = "Chat tarixini avtomatik o'chirish",
            Description =
                "🔴 YOQILSA guruh chatidagi eski xabarlar DOIMIY o'chiriladi va ularni "
                + "ilova orqali TIKLAB BO'LMAYDI (loyihada \"soft delete\" yo'q; yagona "
                + "zaxira — tungi `pg_dump`). O'chirish barcha guruhlarga tegishli, "
                + "arxivlanganlariga ham. "
                + "⚠️ Shaxsiy yozishmalar (kurator ↔ o'quvchi) va jonli dars chatiga "
                + "TEGILMAYDI — faqat guruhning doimiy chati tozalanadi.",
            Kind = SettingValueKind.Toggle,
            Source = SettingSource.Database,
            ConfigurationKey = "Chat:RetentionEnabled",

            // ★ STANDART — O'CHIQ, VA BU ATAYLAB (eng muhim qaror shu yerda).
            //
            // Yoqilgan holda yetkazilsa, yangi versiya chiqqan kuni birinchi
            // yurishda 3 oydan eski BUTUN yozishma yo'q bo'lardi — hech kim
            // so'ramagan, hech kim ogohlantirilmagan holda. Qaytarish yo'li
            // esa faqat tungi dump. Shuning uchun funksiya TAYYOR turadi,
            // lekin uni administrator PANELDAN ongli ravishda yoqadi.
            // Yoqilgan payt log'da ham ko'rinadi (`chat-retention` vazifasi
            // har yurishda nechta qator o'chirganini yozadi).
            DefaultValue = "false",
        },

        new()
        {
            Key = "chat.retention_months",
            Group = SettingGroup.Chat,
            DisplayName = "Chat tarixi saqlanadigan muddat (oy)",
            Description =
                "Guruh chatida shu muddatdan ESKI xabarlar o'chiriladi — Telegram'dagi "
                + "avtomatik o'chirish kabi, SURILIB boruvchi oyna: 3 oy qo'yilsa, har kuni "
                + "\"3 oy oldingi\" xabarlar yo'qolib boradi. "
                + "Faqat yuqoridagi kalit yoqilgan bo'lsa ishlaydi.",
            Kind = SettingValueKind.Number,
            Source = SettingSource.Database,
            ConfigurationKey = "Chat:RetentionMonths",
            DefaultValue = "3",

            // 🔴 PASTKI CHEGARA 1 OY — HALOKATGA QARSHI TO'SIQ, "aql bovar
            // qilmaydigan qiymat" filtri EMAS. `0` qo'yilsa kesim JORIY
            // ONGA to'g'ri kelardi va keyingi yurish (bir soatdan keyin)
            // butun guruh chatini, shu jumladan bugun yozilgan savollarni
            // ham o'chirib yuborardi. Bu qiymat panelda bitta xato bosishga
            // teng, natijasi esa qaytarilmaydi.
            //
            // ⚠️ Chegara IKKI joyda: bu yerda (yozishda 400 beradi va buzuq
            // saqlangan qiymat o'qishda standartga tushadi — `SettingsResolver`)
            // va vazifaning O'ZIDA (`ChatRetentionJob.MinMonths`). Nusxa
            // ataylab: sozlamani qo'lda `AppSettings` jadvaliga yozib
            // qo'yish mumkin, vazifa esa har holda o'chirish oldidan yana
            // bir marta cheklaydi.
            Minimum = 1m,

            // Yuqori chegara 120 oy (10 yil): undan uzun muddat amalda
            // "hech qachon o'chirilmasin" degani va buni yoqilgan kalit
            // bilan emas, kalitni O'CHIRIB ifodalash kerak — aks holda
            // panelda "yoqilgan" ko'rinib, hech narsa qilmaydigan holat
            // paydo bo'lardi.
            Maximum = 120m,
        },

        // ================================================================ XAVFSIZLIK

        new()
        {
            Key = "security.jwt_secret",
            Group = SettingGroup.Security,
            DisplayName = "JWT imzo kaliti",
            Description = "Kirish va yangilash tokenlari shu kalit bilan imzolanadi (HS256).",
            Kind = SettingValueKind.Secret,
            Source = SettingSource.Environment,
            ConfigurationKey = "Jwt:Secret",
            ReadOnlyReason =
                "🔴 IKKI SABAB. (1) Kalit o'zgarsa BARCHA sessiya bir zumda uziladi — dars "
                + "o'rtasida hamma tashqariga chiqarib yuborilardi. (2) Kalit bazadan "
                + "boshqarilsa, admin panelni egallagan odam O'ZI kalit qo'yib, istalgan "
                + "foydalanuvchi nomidan token qalbakilashtira olardi. Kalit faqat serverda, "
                + "muhit o'zgaruvchisida qoladi.",
        },

        new()
        {
            Key = "security.jwt_issuer",
            Group = SettingGroup.Security,
            DisplayName = "Token `iss` qiymati",
            Description = "Tokenni kim chiqarganini bildiruvchi nom. Tekshiruvda ishlatiladi.",
            Kind = SettingValueKind.Text,
            Source = SettingSource.Environment,
            ConfigurationKey = "Jwt:Issuer",
            MaxLength = 100,
            ReadOnlyReason =
                "O'zgartirilsa ALLAQACHON berilgan tokenlar tekshiruvdan o'tmay qoladi — "
                + "ya'ni hamma foydalanuvchi tizimdan chiqib ketardi. " + StartupBoundReason,
        },

        new()
        {
            Key = "security.jwt_audience",
            Group = SettingGroup.Security,
            DisplayName = "Token `aud` qiymati",
            Description = "Token kim uchun berilganini bildiruvchi nom.",
            Kind = SettingValueKind.Text,
            Source = SettingSource.Environment,
            ConfigurationKey = "Jwt:Audience",
            MaxLength = 100,
            ReadOnlyReason =
                "O'zgartirilsa mavjud tokenlar rad etiladi va hamma tizimdan chiqib ketardi. "
                + StartupBoundReason,
        },

        new()
        {
            Key = "security.jwt_access_minutes",
            Group = SettingGroup.Security,
            DisplayName = "Kirish tokeni umri (daqiqa)",
            Description =
                "Kirish tokeni shuncha daqiqa yaroqli. Uzoq bo'lsa o'chirilgan xodim "
                + "shu vaqt davomida tizimda qolaveradi.",
            Kind = SettingValueKind.Number,
            Source = SettingSource.Environment,
            ConfigurationKey = "Jwt:AccessMinutes",
            DefaultValue = "15",
            Minimum = 1m,
            Maximum = 1440m,
            ReadOnlyReason =
                "Bu XAVFSIZLIK oynasi: qiymatni oshirish o'g'irlangan token qancha vaqt "
                + "ishlashini uzaytiradi. Panelni egallagan odam avval shu raqamni "
                + "ko'tarib qo'yardi. " + StartupBoundReason,
        },

        new()
        {
            Key = "security.jwt_refresh_days",
            Group = SettingGroup.Security,
            DisplayName = "Yangilash tokeni umri (kun)",
            Description = "Foydalanuvchi qayta parol kiritmasdan qancha kun ishlay olishi.",
            Kind = SettingValueKind.Number,
            Source = SettingSource.Environment,
            ConfigurationKey = "Jwt:RefreshDays",
            DefaultValue = "7",
            Minimum = 1m,
            Maximum = 365m,
            ReadOnlyReason =
                "Yangilash tokeni bekor qilinmaydi, ya'ni o'g'irlangani o'z muddatigacha "
                + "ishlayveradi — muddatni uzaytirish to'g'ridan-to'g'ri xavfni oshiradi. "
                + StartupBoundReason,
        },

        new()
        {
            Key = "security.postgres_connection",
            Group = SettingGroup.Security,
            DisplayName = "Postgres ulanish satri",
            Description = "Ma'lumotlar bazasiga ulanish (host, foydalanuvchi, parol).",
            Kind = SettingValueKind.Secret,
            Source = SettingSource.Environment,
            ConfigurationKey = "ConnectionStrings:Postgres",
            ReadOnlyReason =
                "🔴 TOVUQ VA TUXUM: sozlamalar bazada saqlanadi, bazaga ulanish esa aynan "
                + "shu satr orqali bo'ladi. Uni bazadan o'qib bo'lmaydi. Xato qiymat "
                + "yozilsa tizim o'zini o'zi butunlay qulflab qo'yardi va faqat serverga "
                + "kirib tuzatish mumkin bo'lardi.",
        },

        new()
        {
            Key = "security.redis_connection",
            Group = SettingGroup.Security,
            DisplayName = "Redis ulanish satri",
            Description = "Sessiya holati, onlayn ro'yxati va chat uchun Redis manzili.",
            Kind = SettingValueKind.Secret,
            Source = SettingSource.Environment,
            ConfigurationKey = "ConnectionStrings:Redis",
            ReadOnlyReason =
                "Ulanish ilova ishga tushganda BIR MARTA ochiladi va butun umr davomida "
                + "qayta ishlatiladi. Bundan tashqari manzil o'zgarsa barcha sessiya holati "
                + "va onlayn ro'yxati yo'qolardi. " + StartupBoundReason,
        },

        new()
        {
            Key = "security.sentry_dsn",
            Group = SettingGroup.Security,
            DisplayName = "Sentry DSN",
            Description = "Xatolarni yuborish manzili. Bo'sh bo'lsa Sentry butunlay o'chiq.",
            Kind = SettingValueKind.Secret,
            Source = SettingSource.Environment,
            ConfigurationKey = "Sentry:Dsn",
            ReadOnlyReason =
                "DSN xatolar oqimini TASHQI xizmatga yo'naltiradi. Bazadan boshqarilsa, "
                + "panelga kirgan odam butun xato oqimini (ichida so'rov konteksti bilan) "
                + "o'z serveriga burib yubora olardi. " + StartupBoundReason,
        },
    ];

    /// <summary>
    /// ════════════════════════════════════════════════════════════════
    /// 🔴 "BREAK-GLASS" MUHIT O'ZGARUVCHILARI — TIZIMNI QAYTA OCHISH KALITI
    /// ════════════════════════════════════════════════════════════════
    ///
    /// Bu ikki kalit BAZADAGI qiymatdan USTUN turadi (registrning odatiy
    /// qoidasidan yagona istisno).
    ///
    /// ★ NIMA UCHUN AYNAN SHU IKKITASI VA BOSHQA HECH BIRI:
    ///
    /// 2026-08-13 dan boshlab tizimga kirishning HAR IKKALA yo'li ham
    /// Telegram botiga tayanadi (email va parol olib tashlandi). Bot
    /// tokeni esa bazada. Ya'ni bu ikki qiymat platformadagi YAGONA
    /// sozlama bo'lib qoldi, uni buzish "hech kim, hech qachon kira
    /// olmaydi" holatiga olib keladi — VA uni tuzatadigan panel ham
    /// o'sha kirish ortida qoladi.
    ///
    /// Boshqa kalitlar bunday emas: buzuq ombor manzili faqat fayllarni,
    /// buzuq LiveKit kaliti faqat jonli darsni to'xtatadi — tizimga
    /// kirib, ularni paneldan tuzatish mumkin qoladi.
    ///
    /// ⚠️ QO'YILGAN BO'LSA — DOIM ISHLAYDI, "faqat avariyada" degan
    ///    texnik cheklov YO'Q. Shuning uchun uni tiklashdan KEYIN
    ///    olib tashlash kerak, aks holda tokenni paneldan almashtirish
    ///    imkoniyati mangu yopiq qoladi. Panel buni ochiq ko'rsatadi:
    ///    maydon qulflanadi va <see cref="Reason"/> matni chiqadi.
    ///
    /// Amaliy tartib: <c>docs/DEPLOY_UBUNTU.md</c>.
    /// </summary>
    public static class TelegramOverrideKeys
    {
        /// <summary>
        /// Konfiguratsiya yo'li. Muhit o'zgaruvchisi ko'rinishi:
        /// <c>Telegram__BotTokenOverride</c> (ASP.NET Core ikki pastki
        /// chiziqni <c>:</c> deb o'qiydi).
        ///
        /// ★ NIMA UCHUN <c>Telegram:BotToken</c> NING O'ZI EMAS: u
        /// allaqachon "baza bo'sh bo'lgandagi BOSHLANG'ICH qiymat"
        /// ma'nosini bildiradi va deploy fayllarida shu ma'noda turibdi.
        /// Ayni kalitga ikkinchi, teskari ma'no berilsa, mavjud
        /// o'rnatishlarda bazadagi token JIMGINA e'tiborsiz qolardi —
        /// ya'ni tuzatish vositasi o'zi buzuvchiga aylanardi.
        /// </summary>
        public const string BotToken = "Telegram:BotTokenOverride";

        /// <summary>Muhit o'zgaruvchisi: <c>Telegram__WebhookSecretOverride</c>.</summary>
        public const string WebhookSecret = "Telegram:WebhookSecretOverride";

        /// <summary>Panelda ko'rinadigan matn (ikkala kalit uchun AYNI).</summary>
        public const string Reason =
            "Bu qiymat hozir MUHIT O'ZGARUVCHISI bilan ustidan yozilgan va u bazadagi "
            + "qiymatdan ustun turibdi. Bu — tizimni qayta ochish uchun qo'yiladigan "
            + "shoshilinch kalit (`Telegram__BotTokenOverride` / "
            + "`Telegram__WebhookSecretOverride`). Paneldan o'zgartirish hech qanday "
            + "ta'sir qilmaydi. Qayta paneldan boshqarish uchun o'zgaruvchini serverdan "
            + "olib tashlab, API'ni qayta ishga tushiring.";
    }

    /// <summary>
    /// Moliya kalitlarining ESKI TIZIMDAGI nomlari. Ular <c>const</c> —
    /// ko'chirish skripti va <c>FinanceSettingsStore</c> AYNI satrga tayanadi.
    /// </summary>
    public static class FinanceKeys
    {
        public const string Threshold = "payment_block_threshold";
        public const string Scope = "payment_block_scope";
    }

    /// <summary>Registrdagi ommaviy kalitlar (kod ichidan murojaat uchun).</summary>
    public static class Keys
    {
        public const string BlockThreshold = "finance.block_threshold";
        public const string BlockScope = "finance.block_scope";
        public const string EnforceBlock = "finance.enforce_block";

        public const string TelegramBotToken = "telegram.bot_token";
        public const string TelegramWebhookSecret = "telegram.webhook_secret";
        public const string TelegramMiniAppUrl = "telegram.mini_app_url";
        public const string TelegramBotUsername = "telegram.bot_username";

        public const string LiveKitApiKey = "livekit.api_key";
        public const string LiveKitApiSecret = "livekit.api_secret";

        public const string StorageServiceUrl = "storage.service_url";

        // 2026-08-13 da paneldan boshqariladigan bo'ldi (sabab registrda).
        public const string StoragePublicUrl = "storage.public_url";

        public const string StorageBucket = "storage.bucket";
        public const string StorageAccessKey = "storage.access_key";
        public const string StorageSecretKey = "storage.secret_key";
        public const string StorageRegion = "storage.region";

        // ---------------------------------------------------------------- WAVE 1
        public const string LessonVideoMaxMb = "lesson.video_max_mb";
        public const string LessonImageMaxMb = "lesson.image_max_mb";

        // ---------------------------------------------------------------- WAVE 3: chat retention
        //
        // Ikkalasini ham FAQAT `ChatRetentionJob` o'qiydi va u ularni HAR
        // YURISHDA qaytadan o'qiydi — ishga tushishda emas. Sabab
        // `JobsSetup` dagi izohda: aks holda panel vazifa umuman qaramaydigan
        // qiymatni tahrirlardi.
        public const string ChatRetentionEnabled = "chat.retention_enabled";
        public const string ChatRetentionMonths = "chat.retention_months";

        // ---------------------------------------------------------------- R5: yozuv ko'rinishi
        //
        // ★ NIMA UCHUN MIGRATSIYASIZ KALIT, YANGI USTUN EMAS: "butun bo'lim
        //   yopilsin" (talabdagi *"entire part of records"*) — markaz
        //   darajasidagi qaror va u yagona qiymat. Uni saqlash uchun jadval
        //   ham, migratsiya ham kerak emas; buning ustiga admin panelida u
        //   BIR SATR frontend kodisiz paydo bo'ladi (registr o'zi forma
        //   quradi).
        //
        // ⚠️ HAR SO'ROVDA O'QILADI (`ISettingsResolver` keshi orqali), ishga
        //   tushishda emas — ya'ni paneldan o'zgartirilsa DARHOL kuchga
        //   kiradi. Aks holda "saqlandi" deyilib, o'quvchilarda hech nima
        //   o'zgarmasdi.
        public const string RecordingsVisibleToStudents = "recordings.visible_to_students";
    }

    /// <summary>
    /// ISH JARAYONIDA o'qiladigan kalitlar — ya'ni <see cref="SettingSource.Database"/>
    /// manbali hammasi.
    ///
    /// ★ NIMA UCHUN ALOHIDA RO'YXAT: keshni to'ldiruvchi
    /// (<c>IRuntimeSettings</c>) AYNAN shu ro'yxatni o'qiydi. "Faqat muhit"
    /// kalitlari keshga UMUMAN tushmaydi — ular baribir o'zgarmaydi, va JWT
    /// kaliti kabi sirlarni butun ilova umri davomida yashaydigan singleton
    /// lug'atda ushlab turishning hech qanday foydasi yo'q, faqat ortiqcha
    /// oshkorlik bo'lardi.
    /// </summary>
    public static IReadOnlyList<SettingDefinition> Runtime { get; } =
        All.Where(d => d.Source == SettingSource.Database).ToArray();

    /// <summary>
    /// Registr o'zi izchilmi. Bu tekshiruv TESTDA chaqiriladi, ish paytida
    /// emas: xato registr — dasturchi xatosi, foydalanuvchi xatosi emas,
    /// shuning uchun uni ishga tushirishda emas, CI'da ushlash kerak.
    /// </summary>
    public static IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        var storageKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in All)
        {
            if (!IsWellFormedKey(definition.Key))
                problems.Add($"'{definition.Key}': kalit faqat a-z, 0-9, '.' va '_' dan iborat bo'lsin.");

            if (!storageKeys.Add(definition.StorageKey))
                problems.Add($"'{definition.Key}': '{definition.StorageKey}' saqlash kaliti takrorlangan.");

            // AppSettings.Key ustuni 100 belgi — undan uzun kalit bazaga sig'maydi.
            if (definition.StorageKey.Length > StorageKeyMaxLength)
                problems.Add($"'{definition.Key}': saqlash kaliti {StorageKeyMaxLength} belgidan uzun.");

            if (definition.Kind == SettingValueKind.Choice && definition.Choices.Count == 0)
                problems.Add($"'{definition.Key}': tanlov turi uchun ro'yxat bo'sh.");

            // Faqat o'qish uchun sabab MAJBURIY — aks holda panelda o'chirilgan
            // maydon izohsiz turardi va foydalanuvchi buni xato deb o'ylardi.
            if (!definition.IsEditable && string.IsNullOrWhiteSpace(definition.ReadOnlyReason))
                problems.Add($"'{definition.Key}': faqat o'qish uchun, lekin sababi yozilmagan.");

            if (definition.Source == SettingSource.Environment
                && string.IsNullOrWhiteSpace(definition.ConfigurationKey))
            {
                problems.Add($"'{definition.Key}': muhit sozlamasi, lekin konfiguratsiya kaliti yo'q.");
            }

            // ── "BREAK-GLASS" KALITINING IZCHILLIGI ────────────────────
            //
            // ★ SABAB MAJBURIY: ustidan yozish kuchga kirganda panel
            //   maydonni QULFLAYDI. Sababsiz qulflangan maydon
            //   administratorni "nega ishlamayapti?" degan savol bilan
            //   yolg'iz qoldirardi — bu registrda `ReadOnlyReason` uchun
            //   allaqachon qabul qilingan qoidaning aynan o'zi.
            if (definition.OverrideConfigurationKey is { Length: > 0 }
                && string.IsNullOrWhiteSpace(definition.OverrideReason))
            {
                problems.Add($"'{definition.Key}': ustidan yozish kaliti bor, lekin sababi yozilmagan.");
            }

            // ★ FAQAT `Database` KALITLARIDA MA'NOGA EGA: `Environment`
            //   kalitlari uchun baza baribir o'qilmaydi, ya'ni "ustidan
            //   yozish" degan tushuncha u yerda bo'sh so'z bo'lardi va
            //   uni ko'rgan odam registrni noto'g'ri tushunardi.
            if (definition.OverrideConfigurationKey is { Length: > 0 }
                && definition.Source != SettingSource.Database)
            {
                problems.Add($"'{definition.Key}': ustidan yozish faqat `Database` manbali kalit uchun.");
            }

            // Standart qiymatning o'zi tekshiruvdan o'tishi shart — aks holda
            // "standartga qaytarish" tugmasi 400 xato beradigan holatga tushardi.
            if (definition.DefaultValue.Length > 0
                && !SettingValueParser.TryNormalize(definition, definition.DefaultValue, out _, out var error))
            {
                problems.Add($"'{definition.Key}': standart qiymat o'z qoidasidan o'tmadi ({error}).");
            }

            if (definition.MinLength > definition.MaxLength)
                problems.Add($"'{definition.Key}': eng qisqa uzunlik eng uzunidan katta.");
        }

        // Bog'langan to'plamlar registrga MOS bo'lishi shart: nomi xato
        // yozilgan kalit jimgina e'tiborsiz qolardi va "TO'LIQ yoki BO'SH"
        // himoyasi o'sha to'plam uchun UMUMAN ishlamasdi.
        foreach (var rule in SettingCoupling.Rules)
        {
            foreach (var key in rule.Keys)
            {
                if (!Index.TryGetValue(key, out var member))
                {
                    problems.Add($"'{rule.Name}' to'plamida registrda yo'q kalit: '{key}'.");
                    continue;
                }

                if (!member.IsEditable)
                    problems.Add($"'{key}': bog'langan to'plam a'zosi, lekin tahrirlanmaydi.");
            }
        }

        return problems;
    }

    private const int StorageKeyMaxLength = 100;

    private static bool IsWellFormedKey(string key)
    {
        if (key.Length == 0)
            return false;

        foreach (var symbol in key)
        {
            var ok = (symbol >= 'a' && symbol <= 'z')
                     || (symbol >= '0' && symbol <= '9')
                     || symbol == '.'
                     || symbol == '_';

            if (!ok)
                return false;
        }

        return true;
    }

    /// <summary>Guruhlarni e'lon tartibida qaytaradi (panel shu tartibda chizadi).</summary>
    public static IReadOnlyList<SettingGroup> Groups { get; } =
        All.Select(d => d.Group).Distinct().ToArray();

    /// <summary>Diagnostika uchun qisqa tavsif.</summary>
    public static string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"SettingsRegistry: {All.Count} sozlama, {Groups.Count} guruh.");
}
