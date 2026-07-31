namespace Zinnur.Application.Jobs;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// LEADER LOCK — "shu vazifani HOZIR faqat men bajaraman" huquqi.
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN UMUMAN KERAK: platforma gorizontal masshtablanadi, ya'ni API
/// bir nechta konteynerda ishlaydi va fon rejalashtiruvchisi HAR BIRIDA
/// ko'tariladi. Qulfsiz holatda oylik to'lov yozuvlari ikki instance
/// tomonidan bir vaqtda ochilardi, muddati o'tgan dars esa ikki marta
/// yakunlanib, o'quvchilarga ikkita "dars tugadi" xabari ketardi.
///
/// Eski tizim (`APScheduler`) aynan shu joyda sinardi: rejalashtiruvchi
/// ilova jarayoni ICHIDA edi va ikkinchi instance ko'tarilishi bilan har
/// vazifa ikki marta bajarilardi.
///
/// ── NIMA UCHUN OUTBOX'DAGIDEK `FOR UPDATE SKIP LOCKED` EMAS ─────────────
///
/// Notifikatsiya navbatida ish QATORLARGA bo'linadi: har xabar mustaqil,
/// shuning uchun ikki worker parallel ishlashi FOYDALI (izoh:
/// <c>IOutboxStore</c>). Bu yerda esa ish BO'LINMAYDI — "iyul oyini ochish"
/// bitta yaxlit amal. Uni ikkiga bo'lib bo'lmaydi, faqat bittasiga
/// topshirish mumkin.
///
/// ── QAYSI MEXANIZM VA NEGA ──────────────────────────────────────────────
///
/// Amalga oshirilishi — PostgreSQL <c>pg_try_advisory_lock</c>
/// (<c>PostgresAdvisoryJobLock</c>). Sabablari:
///
///  1) JADVAL KERAK EMAS -> migratsiya ham kerak emas, model o'zgarmaydi.
///
///  2) QULF SESSIYAGA (ulanishga) bog'langan: instance qulasa, TCP ulanishi
///     uzilishi bilan Postgres qulfni O'ZI bo'shatadi va boshqa instance
///     DARHOL egallaydi. Qulf jadvalida esa "egasi + muddat" yozilardi va
///     qulagan instance'dan keyin butun tizim muddat tugagunicha (masalan
///     2 daqiqa) KUTARDI — bu esa aynan eng yomon paytda, ya'ni nosozlik
///     paytida sekinlashish demakdir.
///
///  3) MUDDAT YO'Q -> "heartbeat" ham kerak emas: ish 10 daqiqa davom etsa
///     ham qulf ushlab turiladi, chunki u vaqtga emas, ULANISHGA bog'liq.
///     Ulanish tirikligini Npgsql'ning <c>Keepalive</c> paketi ta'minlaydi.
///
/// ⚠️ NOZIK JOYI VA UNING YECHIMI: advisory lock SESSIYAGA bog'langan, EF
/// Core esa ulanishlar HOVUZIDAN foydalanadi. Qulfni olgan ulanish hovuzga
/// qaytsa, uni boshqa so'rov olardi va o'sha so'rov tugaganda... qulf
/// baribir ochiq qolardi (chunki sessiya yopilmaydi) — ya'ni qulf abadiy
/// band bo'lib qolishi ham, tasodifan boshqa kod tomonidan bo'shatilishi ham
/// mumkin edi. Shuning uchun qulf uchun ALOHIDA, <c>Pooling=false</c> bilan
/// ochilgan ulanish ishlatiladi: u hovuzga umuman tushmaydi va
/// <see cref="IJobLockHandle"/> yopilganda fizik ravishda uziladi.
///
/// ── NIMA UCHUN REDIS EMAS ───────────────────────────────────────────────
///
/// Redis'da qulf texnik jihatdan osonroq (<c>SET NX PX</c>), lekin u AYRIM
/// tizim: Redis o'chib qolsa yoki kaliti tozalansa qulf yo'qoladi va vazifa
/// IKKI MARTA ketardi. Bu vazifalar esa Postgres'dagi ma'lumot butunligiga
/// tegadi (to'lov yozuvi, davomat). Qulf ma'lumot bilan AYNI bazada bo'lsa,
/// "baza tirik, lekin qulf yo'qolgan" holati umuman mumkin emas: baza
/// yiqilsa vazifaning o'zi ham bajarilmaydi.
/// </summary>
public interface IJobLock
{
    /// <summary>
    /// Vazifa nomi bo'yicha qulfni olishga URINADI. Hech qachon KUTMAYDI.
    /// </summary>
    /// <param name="jobName">
    /// Qulf makoni. Har vazifaga O'Z qulfi: uzoq davom etgan dars yakunlash
    /// oylik to'lov vazifasini to'sib qo'ymasin, va ikki instance turli
    /// vazifalarni parallel bajara olsin.
    /// </param>
    /// <returns>
    /// Qulf olingan bo'lsa — uni USHLAB TURADIGAN tutqich; qulf boshqa
    /// instance'da bo'lsa — <c>null</c> (bu XATO emas, normal holat).
    /// </returns>
    Task<IJobLockHandle?> TryAcquireAsync(string jobName, CancellationToken ct = default);
}

/// <summary>
/// Olingan qulf. Yopilishi bilan qulf bo'shaydi.
///
/// ★ <see cref="IAsyncDisposable"/> ATAYLAB: qulfni bo'shatish bazaga
/// so'rov yuborish va ulanishni yopishdir, ya'ni I/O. Sinxron
/// <c>Dispose</c> bo'lsa u thread'ni bloklardi.
/// </summary>
public interface IJobLockHandle : IAsyncDisposable
{
    /// <summary>Qaysi vazifa uchun olingan.</summary>
    string JobName { get; }

    /// <summary>
    /// Qulf HALI HAM bizdami — uzoq ishdan keyin tekshirish uchun.
    ///
    /// NIMA UCHUN KERAK: ulanish tarmoq nosozligi tufayli uzilsa, Postgres
    /// qulfni bo'shatadi va boshqa instance uni egallashi mumkin. Bunday
    /// holatda ishning natijasi ikkinchi instance bilan kesishgan bo'lishi
    /// mumkin — buni hech bo'lmaganda LOGDA ko'rish kerak, aks holda
    /// "nega ikkita yozuv paydo bo'ldi?" degan savol javobsiz qolardi.
    /// </summary>
    Task<bool> IsHeldAsync(CancellationToken ct = default);
}
