using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Settings;
using Zinnur.Application.Settings.Services;

namespace Zinnur.Application.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// GURUH CHATI TARIXINI AVTOMATIK TOZALASH (Telegram uslubidagi retention)
/// ════════════════════════════════════════════════════════════════════════
///
/// Egasining talabi: "3 oy belgilansa guruhdagi 3 oy oldingi yozishmalar
/// DOIMIY o'chirilib borishi kerak". Ya'ni bir martalik tozalash emas —
/// SURILIB boruvchi oyna: har yurishda "bugundan N oy oldin" chizig'i
/// oldinga siljiydi va undan eskisi yo'qoladi.
///
/// ── QAMROV: FAQAT <see cref="IApplicationDbContext.GroupChatMessages"/> ─
///
/// 🔴 SHAXSIY YOZISHMALAR (<c>DirectMessages</c>, kurator ↔ o'quvchi) VA
/// JONLI DARS CHATI (<c>ChatMessages</c>) TEGILMAYDI. Uchalasi alohida
/// jadval (sabab: <c>GroupChatMessage</c> sinfi izohi) va talabda AYNAN
/// "GURUHDAGI yozishmalar" deyilgan. Ataylab tor o'qildi:
///
///   • shaxsiy yozishma — ko'pincha xodimning o'quvchi bilan ISHI haqidagi
///     yagona iz ("ota-onasiga qo'ng'iroq qilindi", "to'lov kelishuvi").
///     Uni "chat tarixi" degan umumiy so'z ostida jimgina o'chirish
///     talabdan KENGROQ va qaytarib bo'lmaydigan qaror bo'lardi;
///   • jonli dars chati sessiyaga bog'langan va dars yozuvi bilan birga
///     o'sha darsning hujjati hisoblanadi.
///
/// Kerak bo'lsa ular uchun ALOHIDA kalit qo'shiladi — teskarisi (o'chirib
/// bo'lingan yozishmani qaytarish) mumkin emas.
///
/// ── NIMA UCHUN QATTIQ (HARD) O'CHIRISH ─────────────────────────────────
///
/// Loyihada soft-delete infratuzilmasi UMUMAN yo'q (<c>IsDeleted</c> —
/// bitta ham natija bermaydi). Uni shu ish uchun kiritish har o'qish
/// yo'liga (sahifalash, o'qilmaganlar sanog'i, oxirgi xabar ko'rinishi)
/// filtr qo'shishni talab qilardi — bitta joyda unutilsa "o'chirilgan"
/// xabar qaytib chiqardi. Bundan tashqari talabning ma'nosi aynan JOY
/// BO'SHATISH va "yozishmalar qolmasin": belgilangan qator diskda
/// qolaverardi.
///
/// ── 🔴 TIKLASH YO'LI (o'chirilgandan keyin) ────────────────────────────
///
/// Ilova orqali YO'Q. Yagona manba — tungi `pg_dump`
/// (<c>infra/scripts/backup-db.sh</c>, har kuni 03:15, gzip, 14 kun
/// saqlanadi). Tiklash tartibi:
///   1) kerakli sanadagi `zinnur-YYYYmmdd-HHMM.sql.gz` topiladi;
///   2) u ALOHIDA (vaqtinchalik) bazaga tiklanadi — ishlayotgan bazaga
///      EMAS, aks holda dumpdan keyingi barcha yozuvlar yo'qoladi;
///   3) `GroupChatMessages` dan kerakli guruh/oqim qatorlari `INSERT`
///      bilan ko'chiriladi (`Id` saqlanadi — o'qilganlik belgilari AYNAN
///      shu raqamlarga tayanadi).
/// Oxirgi dumpdan KEYIN yozilgan va o'chirilgan xabar qaytmaydi.
///
/// ── NIMA UCHUN SOZLAMA VAZIFA ICHIDA O'QILADI ──────────────────────────
///
/// 🔴 Ikkala qiymat ham HAR YURISHDA <see cref="ISettingsResolver"/> orqali
/// qaytadan o'qiladi, konstruktorda EMAS. Egasining talabi — panel orqali
/// boshqarish; qiymat DI'da qotib qolsa panel "saqlandi" derdi-yu, vazifa
/// eski qiymat bilan ishlayverardi (registr izohidagi "jimgina yolg'on").
/// Shu sababli vazifa <c>JobsSetup</c> da SHARTSIZ ro'yxatdan o'tadi:
/// yoqilgan-yoqilmagani muhit bayrog'i emas, SOZLAMA.
///
/// ── NIMA UCHUN `SentAt` GA ALOHIDA INDEKS QO'SHILDI ────────────────────
///
/// <c>GroupChatMessages</c> — eng katta jadval va uning mavjud ikkala
/// indeksi ham <c>GroupId</c> dan boshlanadi. Ya'ni <c>WHERE SentAt &lt;
/// cutoff</c> KETMA-KET SKAN bo'lardi — har yurishda, hatto o'chiradigan
/// narsa YO'Q bo'lganda ham. Shuning uchun migratsiya bilan
/// <c>IX_GroupChatMessages_SentAt</c> qo'shildi <c>(SentAt, Id)</c>.
///
/// ★★ AVVAL BOSHQA YO'L TANLANGAN EDI VA U RAD ETILDI — YOZIB QO'YILADI,
///    CHUNKI U JUDA JOZIBALI KO'RINADI:
///
///   "Indeks shart emas: <c>Id</c> — `bigserial`, <c>SentAt</c> esa yozish
///    paytida qo'yiladi, ya'ni ikkalasi bir xil tartibda o'sadi. Kesimni
///    BIR MARTA `Id` ga aylantirsak (birlamchi kalit bo'yicha ikkilik
///    qidiruv), butun ish mavjud indeks ustida bajariladi."
///
/// 🔴 NIMA UCHUN RAD ETILDI: bu yo'l YOZILMAGAN taxminga tayanadi —
/// "`SentAt` tartibi `Id` tartibi bilan bir xil". Taxminni HECH NARSA
/// qo'riqlamaydi: <c>SentAt</c> — oddiy o'rnatiladigan xossa, ko'chirish
/// vositasi esa `Id` ni ham, `SentAt` ni ham ESKI bazadan yozadi
/// (<c>tools/migration</c>: <c>chat_messages -> GroupChatMessages</c>).
/// Bitta backfill yoki ma'lumot tuzatishi tartibni buzsa, ikkilik qidiruv
/// noto'g'ri chegara topadi va vazifa HECH NIMA O'CHIRMAY qo'yadi —
/// xatosiz, log'siz, JIMGINA. Ya'ni egasi so'ragan funksiya "yoqilgan"
/// ko'rinib turib ishlamay qolardi.
///
/// ⚠️ Bu faraziy xavf emas: aynan shu holat sinovda yuz berdi — testlar
/// xabarlarni yozgandan KEYIN eskirtiradi (boshqa yo'l yo'q: `SentAt` ni
/// server qo'yadi) va tozalash jimgina to'xtab qoldi.
///
/// Indeksning narxi esa kichik: guruh chatiga xabarni ODAM yozadi, ya'ni
/// bu yozish yo'li mashina tezligida emas. Bitta qo'shimcha B-tree yozuvi
/// — mikrosoniyalar; buning evaziga o'chirish so'rovi ODDIY va KO'Z BILAN
/// TO'G'RILIGI KO'RINADIGAN indeks bo'ylab o'qishga aylanadi. Qaytarib
/// bo'lmaydigan amal uchun bu almashuv arziydi.
///
/// ★ INDEKSDA `Id` IKKINCHI USTUN: paket AYNAN shu tartibda olinadi
/// (<c>ORDER BY SentAt, Id</c>) va qatorlar indeksning O'ZIDAN o'qiladi —
/// jadvalga kirilmaydi. `Id` bo'lmasa Postgres bir xil `SentAt` li
/// qatorlarni saralash uchun jadvalga qaytardi.
///
/// ── O'QILGANLIK BELGILARI (<c>GroupChatReads</c>) ──────────────────────
///
/// ⚠️ ATAYLAB TEGILMAYDI. Belgi o'chirilgan xabarning `Id` siga ishora
/// qilib qolishi mumkin — va bu ZARARSIZ:
///
///   • jadvallar orasida FK YO'Q (`LastReadMessageId` — oddiy son), ya'ni
///     o'chirish yaxlitlikni buzmaydi;
///   • o'qilmaganlar sanog'i FAQAT MAVJUD qatorlar ustida hisoblanadi
///     (`Id > lastRead` bo'yicha `COUNT`), ya'ni o'chirilgan xabar sanoqqa
///     ham, oldin ham, keyin ham kirmaydi — sanoq har doim aniq qoladi;
///   • `Id` — global o'suvchi ketma-ketlik, ya'ni tozalangandan KEYIN
///     kelgan xabarning raqami eski belgidan HAR DOIM katta bo'ladi va
///     yangi xabar to'g'ri "o'qilmagan" bo'lib ko'rinadi.
///
/// TO'LIQ BO'SHAGAN oqimda belgi "yetim" bo'lib qoladi:
/// <c>GroupChatRead.Advance</c> ORQAGA ketmaydi, `MarkReadAsync` esa oqim
/// oxiriga (endi `0`) qirqadi — ya'ni belgi eski qiymatida turaveradi va
/// hech narsani buzmaydi (o'qilmaganlar `0`). Uni o'chirish HECH NIMANI
/// o'zgartirmasdi, lekin `MarkReadAsync` dagi unikal indeks poygasi bilan
/// kesishadigan ikkinchi yozish yo'lini paydo qilardi — shuning uchun
/// belgilar qoldiriladi (bu xatti-harakat test bilan qulflangan).
///
/// ── IDEMPOTENTLIK ──────────────────────────────────────────────────────
///
/// Vazifa hech qanday holat saqlamaydi (oxirgi yurish vaqti ham, kursor
/// ham yo'q): har yurish kesimni QAYTADAN hisoblaydi va "kesimdan eski
/// qator bormi?" deb so'raydi. Ikkinchi yurish hech nima topmaydi —
/// javob indeksdan BITTA arzon so'rov bilan olinadi. Yurish o'rtasida
/// instance qulasa ham zarar yo'q: o'chirilgan paket allaqachon commit
/// bo'lgan, qolganini keyingi yurish o'sha joydan davom ettiradi.
/// </summary>
public sealed class ChatRetentionJob(
    IApplicationDbContext db,
    ISettingsResolver settings,
    TimeProvider clock,
    ChatRetentionSettings options,
    ILogger<ChatRetentionJob> logger) : IScheduledJob
{
    /// <summary>
    /// 🔴 ENG QISQA RUXSAT ETILGAN MUDDAT — vazifaning O'ZIDAGI to'siq.
    ///
    /// Registrdagi <c>Minimum = 1</c> yozish yo'lini qo'riqlaydi, bu esa
    /// O'CHIRISH yo'lini: `AppSettings` jadvaliga qo'lda yozilgan `0`
    /// (yoki ko'chirish skripti qoldirgan qiymat) kesimni JORIY ONGA
    /// tenglashtirib, butun chatni yo'q qilardi. Nusxa ataylab — narxi
    /// bitta `Math.Clamp`, muqobili esa qaytarilmaydigan ma'lumot yo'qotish.
    /// </summary>
    public const int MinMonths = 1;

    /// <summary>Eng uzun muddat — registrdagi <c>Maximum</c> bilan bir xil.</summary>
    public const int MaxMonths = 120;

    /// <inheritdoc />
    public string Name => "chat-retention";

    /// <inheritdoc />
    public TimeSpan Interval => options.Interval;

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(CancellationToken ct = default)
    {
        // ── 1) SIYOSAT: har yurishda paneldan qaytadan o'qiladi ──────────
        //
        // Ikkala kalit BITTA so'rov bilan olinadi (`ResolveManyAsync`) —
        // ikkitasi uchun ikki marta bazaga borishning sababi yo'q.
        var resolved = await settings
            .ResolveManyAsync([EnabledSetting, MonthsSetting], ct)
            .ConfigureAwait(false);

        var enabled = SettingValueParser.TryReadBool(resolved[0].Value, out var flag) && flag;

        if (!enabled)
        {
            // `Debug`: vazifa har soatda yuradi va o'chiq holat NORMAL —
            // `Information` bo'lsa log foydali narsani ko'mib tashlardi.
            JobLog.RetentionDisabled(logger);
            return JobRunResult.Nothing;
        }

        var months = ResolveMonths(resolved[1]);
        var cutoff = clock.GetUtcNow().AddMonths(-months);

        // ── 2) PAKETLAB O'CHIRAMIZ ──────────────────────────────────────
        var (deleted, batches, capped) = await PurgeAsync(cutoff, ct).ConfigureAwait(false);

        if (deleted == 0)
            return JobRunResult.Nothing;

        // 🔴 HAR YURISH LOGDA KO'RINADI. Qaytarib bo'lmaydigan amal jimgina
        // bajarilmasligi kerak: "nega bu guruhda eski xabarlar yo'q?" degan
        // savolga javob AYNAN shu qatordan topiladi.
        JobLog.ChatHistoryPurged(logger, deleted, months, cutoff, batches);

        if (capped)
            JobLog.ChatHistoryPurgeCapped(logger, deleted, options.MaxBatchesPerRun);

        return new JobRunResult(
            Processed: deleted,
            Skipped: 0,
            Note: string.Create(
                CultureInfo.InvariantCulture,
                $"{months} oy, kesim {cutoff:yyyy-MM-dd}{(capped ? ", chegaraga yetdi" : string.Empty)}"));
    }

    // ================================================================= muddat

    /// <summary>
    /// Saqlangan qiymatni oyga aylantiradi va QAT'IY cheklaydi.
    ///
    /// Buzuq qiymat vazifani yiqitmaydi — registrdagi standartga tushadi
    /// (<c>LessonAssetService.LimitBytesAsync</c> bilan AYNI naqsh).
    /// </summary>
    private static int ResolveMonths(ResolvedSetting resolved)
    {
        var months = SettingValueParser.TryReadDecimal(MonthsSetting, resolved.Value, out var value)
            ? (int)value
            : int.Parse(MonthsSetting.DefaultValue, CultureInfo.InvariantCulture);

        return Math.Clamp(months, MinMonths, MaxMonths);
    }

    // ================================================================= o'chirish

    /// <summary>
    /// Kesimdan eski qatorlarni PAKETLAB o'chiradi.
    ///
    /// ★ HAR PAKET IKKI SO'ROV: (1) <c>IX_GroupChatMessages_SentAt</c>
    /// bo'yicha keyingi paketning `Id` lari o'qiladi, (2) AYNAN o'sha
    /// `Id` lar birlamchi kalit bo'yicha o'chiriladi.
    ///
    /// ★ NIMA UCHUN "TANLA, KEYIN O'CHIR" — bir so'rovli
    /// <c>DELETE ... WHERE SentAt &lt; cutoff</c> EMAS: chegarasiz `DELETE`
    /// millionlab qatorni BITTA tranzaksiyada o'chirardi — uzoq tranzaksiya,
    /// katta WAL va replikatsiya kechikishi, ya'ni fon tozalashi ilovaning
    /// O'ZINI sekinlashtirardi (`SessionAutoCloseJob` dagi `BatchSize` bilan
    /// bir xil sabab). Tanlangan `Id` lar bo'yicha o'chirish esa AYNAN
    /// ko'rilgan qatorlarni o'chiradi — ortiqcha bitta ham qator emas.
    ///
    /// ★ KURSOR KERAK EMAS: o'chirilgan qator keyingi so'rovda shartga
    /// TUSHMAYDI, ya'ni har paket albatta oldinga siljiydi.
    ///
    /// ★ NIMA UCHUN BIR YURISHDA CHEGARA BOR: birinchi yoqilganda orqada
    /// yillik tarix turgan bo'lishi mumkin. Chegarasiz birinchi yurish
    /// soatlab davom etib, qulfni ushlab turardi. Chegaraga yetilsa vazifa
    /// natijani logga yozib chiqadi va keyingi yurishda DAVOM ETADI.
    ///
    /// ★ NIMA UCHUN <c>ExecuteDeleteAsync</c> (`SaveChanges` emas): 5000
    /// qatorni xotiraga yuklab, kuzatuvchiga qo'yib, keyin o'chirish o'nlab
    /// barobar qimmat va bu yerda entity'ning O'ZI kerak emas — Domain
    /// qoidasi yo'q, faqat qator yo'qotiladi.
    /// </summary>
    private async Task<(int Deleted, int Batches, bool Capped)> PurgeAsync(
        DateTimeOffset cutoff, CancellationToken ct)
    {
        var deleted = 0;
        var batches = 0;

        while (batches < options.MaxBatchesPerRun)
        {
            ct.ThrowIfCancellationRequested();

            var ids = await db.GroupChatMessages.AsNoTracking()
                .Where(m => m.SentAt < cutoff)
                .OrderBy(m => m.SentAt)
                .ThenBy(m => m.Id)
                .Take(options.BatchSize)
                .Select(m => m.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (ids.Count == 0)
                return (deleted, batches, false);

            deleted += await db.GroupChatMessages
                .Where(m => ids.Contains(m.Id))
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);

            batches++;

            // To'liq bo'lmagan paket — oxirgisi, keyingi so'rov ortiqcha.
            if (ids.Count < options.BatchSize)
                return (deleted, batches, false);
        }

        return (deleted, batches, true);
    }

    // ================================================================= yordamchi

    private static readonly SettingDefinition EnabledSetting =
        Definition(SettingsRegistry.Keys.ChatRetentionEnabled);

    private static readonly SettingDefinition MonthsSetting =
        Definition(SettingsRegistry.Keys.ChatRetentionMonths);

    private static SettingDefinition Definition(string key) =>
        SettingsRegistry.TryGet(key, out var definition)
            ? definition

            // FAQAT registr buzilganda — dasturchi xatosi. Jimgina "o'chiq"
            // holatga tushish funksiyaning ishlamayotganini yashirardi.
            : throw new InvalidOperationException($"Registrda '{key}' sozlamasi yo'q.");
}

/// <summary>
/// Tozalashning TEXNIK chegaralari — <see cref="SessionAutoCloseSettings"/>
/// bilan bir xil sabab: Application qatlami konfiguratsiya tizimini bilmaydi.
///
/// ★ NIMA UCHUN SIYOSAT BU YERDA EMAS: "yoqilganmi" va "necha oy" —
/// egasining qarori va u PANELDAN o'zgaradi, ya'ni ular vazifa ichida,
/// sozlamalar registridan o'qiladi. Bu yerda esa faqat "qanchalik tez-tez
/// va qanday katta bo'laklarda" degan EKSPLUATATSIYA parametrlari qoladi —
/// ular muhit o'zgaruvchisi bilan sozlanadi va administratorga ko'rsatilmaydi.
/// </summary>
/// <param name="Interval">Ikki yurish orasidagi masofa.</param>
/// <param name="BatchSize">Bitta <c>DELETE</c> da ko'pi bilan nechta qator.</param>
/// <param name="MaxBatchesPerRun">Bitta yurishdagi paketlar chegarasi.</param>
public sealed record ChatRetentionSettings(
    TimeSpan Interval,
    int BatchSize,
    int MaxBatchesPerRun);
