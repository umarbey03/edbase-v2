using System.Globalization;

namespace Zinnur.WebApi.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// 🔴 PROD'DA DEV QIYMATLARI BILAN KO'TARILMASLIK — DARVOZA
/// ════════════════════════════════════════════════════════════════════════
///
/// NIMA UCHUN QO'SHILDI (2026-08-22 auditi). Ilova sirlarning MAVJUDLIGINI
/// tekshirardi (<c>Jwt:Secret</c> bor va 32 belgidan uzun, <c>LiveKit</c>
/// kalitlari to'liq), lekin ularning QANDAY qiymat ekanini tekshirmasdi.
/// Ya'ni <c>.env.example</c> dagi namuna qiymatlar prod serverga o'z
/// holicha ko'chirilsa, ilova MUAMMOSIZ ko'tarilardi va:
///
///   • <c>Jwt:Secret</c> ommaviy repozitoriyada turgan satr bo'lardi —
///     uni bilgan HAR KIM istalgan foydalanuvchi nomidan yaroqli token
///     yasay olardi (rol ham o'ziniki, ya'ni administrator ham);
///   • <c>LiveKit:ApiKey=devkey</c> — LiveKit misollaridagi ommaviy
///     qiymat; secret bilan birga istalgan xonaga HOST huquqi beradi;
///   • baza paroli <c>zinnur_dev_only_change_me</c> bo'lardi.
///
/// Bularning HECH BIRI ishlash paytida bilinmasdi: xato ham, ogohlantirish
/// ham chiqmasdi — tizim "ishlab turgan" ko'rinardi. Aynan shu sababdan
/// tekshiruv ISHGA TUSHISHDA va OCHIQ YIQILISH bilan bo'ladi.
///
/// ──────────────────────────────────────────────────────────────────────
/// ★ QOIDA — MARKER SO'ZLAR, RO'YXAT EMAS
///
/// Har bir dev standarti <c>.env.example</c> da ATAYLAB marker bilan
/// yozilgan: <c>dev_only_...</c>, <c>..._change_me</c>. Tekshiruv aynan
/// shu markerlarni izlaydi, aniq qiymatlar ro'yxatini emas. Sabab: ro'yxat
/// eskiradi — yangi dev standarti qo'shilganda kimdir uni bu yerga
/// qo'shishni unutardi va darvoza JIMGINA teshik bo'lib qolardi. Marker
/// esa konvensiyaning O'ZI bilan birga keladi.
///
/// <c>devkey</c> — alohida holat: unda marker yo'q, chunki u BIZNING
/// standartimiz emas, LiveKit'niki.
///
/// ──────────────────────────────────────────────────────────────────────
/// ★ HAMMA MUAMMO BIRDANIGA AYTILADI
///
/// Birinchi xatoda to'xtasak, operator sirlarni BITTALAB tuzatib, har
/// safar qayta deploy qilishga majbur bo'lardi (har urinish — bir necha
/// daqiqa). Shuning uchun tekshiruvlar to'liq yig'iladi va bitta
/// xabarda qaytariladi.
///
/// ⚠️ FAQAT <c>Production</c> DA ISHLAYDI. <c>Development</c> va
///    <c>Staging</c> tegilmaydi — integratsion testlar ham
///    (<c>ZinnurApiFactory</c>) aynan dev qiymatlari bilan ishlaydi.
/// </summary>
public static class ProductionSecretsGuard
{
    /// <summary>
    /// Dev standartlariga qo'yilgan markerlar. Qiymatda shulardan biri
    /// UCHRASA — u namuna qiymat, ya'ni prod uchun yaroqsiz.
    /// </summary>
    private static readonly string[] DevMarkers = ["dev_only", "change_me"];

    /// <summary>
    /// Mahalliy manzillar — <c>Cors:AllowedOrigins</c> prod'da bularga
    /// ishora qilmasligi kerak.
    /// </summary>
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "0.0.0.0", "::1"];

    /// <summary>
    /// Tekshiradi va muammo bo'lsa <see cref="InvalidOperationException"/>
    /// bilan ilovani TO'XTATADI.
    /// </summary>
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!environment.IsProduction()) return;

        var problems = new List<string>();

        // ---- sirlar: namuna qiymat qolib ketmaganmi ----
        CheckNotSample(configuration, "Jwt:Secret", problems);
        CheckNotSample(configuration, "LiveKit:ApiSecret", problems);
        CheckNotSample(configuration, "Storage:SecretKey", problems);
        CheckNotSample(configuration, "Storage:AccessKey", problems);

        // ══════════════════════════════════════════════════════════════
        // ⚠️ `ConnectionStrings:Postgres` ATAYLAB TEKSHIRILMAYDI
        //
        // Bu qiymat dastlab ro'yxatda bor edi va olib tashlandi. Sabab —
        // u YAGONA kalit bo'lib, "namuna" va "haqiqiy, lekin dev bazasi"
        // ni ajratib bo'lmaydi:
        //
        //   `DevQuickLoginProductionApiFactory` ilovani ATAYLAB
        //   `Production` muhitida ko'taradi (quick-login darvozasini
        //   aynan shu muhitda sinash uchun), lekin DEV bazasiga ulanadi
        //   — uning paroli esa `zinnur_dev_only_change_me`. Ya'ni
        //   tekshiruv o'zining xavfsizlik testini yiqitardi va bu
        //   "tuzatish" uchun darvozaga umumiy o'chirgich qo'yish
        //   kerak bo'lardi — aynan shu narsadan qochilmoqda.
        //
        // ★ YO'QOTISH KICHIK: prod'da Postgres tashqariga UMUMAN
        //   chiqarilmaydi (`docker-compose.prod.yml` da `ports` yo'q),
        //   ya'ni paroli sizib chiqsa ham unga tashqaridan yetib
        //   bo'lmaydi. Yuqoridagi to'rt kalit esa aksincha — ular
        //   bilan tashqaridan token yasash yoki xonaga kirish mumkin.
        //
        // ★ O'RNIGA: parolni tasodifiy qilish `DEPLOY_UBUNTU.md` 7.1 da
        //   deploy qadamining O'ZIGA kiritilgan (`openssl rand`), ya'ni
        //   namuna parol prod `.env` ga umuman yozilmaydi.
        // ══════════════════════════════════════════════════════════════

        // ---- LiveKit'ning ommaviy namuna kaliti ----
        var liveKitKey = configuration["LiveKit:ApiKey"];

        if (string.Equals(liveKitKey?.Trim(), "devkey", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                "LiveKit:ApiKey = \"devkey\" — bu LiveKit hujjatlaridagi OMMAVIY qiymat. "
                + "Tasodifiy nom qo'ying (masalan `zinnur$(openssl rand -hex 6)`) va uni "
                + "`LIVEKIT_KEYS` bilan BAYTMA-BAYT mos qiling.");
        }

        // ---- ombor prod'da MinIO'ga ishora qilmasin ----
        var storageUrl = configuration["Storage:ServiceUrl"];

        if (!string.IsNullOrWhiteSpace(storageUrl)
            && storageUrl.Contains("minio", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                "Storage:ServiceUrl hali DEV MinIO'ga ishora qilyapti (\"" + storageUrl + "\"). "
                + "Prod'da fayllar Cloudflare R2 da: `R2_SERVICE_URL` ni sozlang "
                + "(docker-compose.prod.yml dagi Storage bloki).");
        }

        // ---- CORS: mahalliy manzil prod'da qolib ketmasin ----
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        foreach (var origin in origins)
        {
            if (string.IsNullOrWhiteSpace(origin)) continue;

            foreach (var local in LocalHosts)
            {
                if (!origin.Contains(local, StringComparison.OrdinalIgnoreCase)) continue;

                problems.Add(
                    "Cors:AllowedOrigins da mahalliy manzil bor (\"" + origin + "\"). "
                    + "Prod'da u frontend'ning HAQIQIY domeni bo'lishi kerak "
                    + "(masalan `https://app.domen.uz`).");

                break;
            }
        }

        if (problems.Count == 0) return;

        var numbered = problems
            .Select((text, index) =>
                "  " + (index + 1).ToString(CultureInfo.InvariantCulture) + ") " + text);

        throw new InvalidOperationException(
            "🔴 PRODUCTION SOZLAMASI YAROQSIZ — ilova ATAYLAB ishga tushmadi.\n\n"
            + "Quyidagi qiymatlar hali NAMUNA (`.env.example`) holatida:\n\n"
            + string.Join("\n\n", numbered)
            + "\n\nHaqiqiy sirlarni yaratish: `docs/DEPLOY_UBUNTU.md`, 7.1-bo'lim.\n"
            + "Bu tekshiruvni o'chirish YO'LI YO'Q — u ataylab shunday.");
    }

    /// <summary>Bitta kalitni namuna markerlariga tekshiradi.</summary>
    private static void CheckNotSample(
        IConfiguration configuration, string key, List<string> problems)
    {
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value)) return;

        foreach (var marker in DevMarkers)
        {
            if (!value.Contains(marker, StringComparison.OrdinalIgnoreCase)) continue;

            problems.Add(
                key + " hali namuna qiymatda (ichida \"" + marker + "\" bor). "
                + "Yangi qiymat: `openssl rand -base64 48`.");

            return;
        }
    }
}
