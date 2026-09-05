namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TUNGI NAVBAT — QATORNI EGALLASH PORTI
/// ════════════════════════════════════════════════════════════════════════
///
/// ── 🔴 MUTLAQ MUSTASNOLIK — BAZANING QATORI, `IJobLock` EMAS ────────────
///
/// Boshqa fon vazifalari Postgres advisory lock ostida yuradi
/// (<c>IJobRunner</c>). Bu yerda u ISHLAMAYDI: advisory lock butun ish
/// davomida ALOHIDA ULANISHNI ushlab turadi, ffmpeg esa 90 daqiqa
/// ishlaydi. Shu vaqt ichida tarmoqning bir lahzalik uzilishi qulfni
/// yo'qotardi (<c>JobRunner</c> buning uchun <c>LockLost</c>
/// ogohlantirishini allaqachon yozadi) va IKKI kodlovchi BITTA kalitga
/// yozib qo'yardi — natija: buzuq mp4 va uni hech kim sezmaydi.
///
/// Ijara esa oddiy USTUN: u yo'qolmaydi, shunchaki eskiradi. Ishchi
/// qulasa qator muddat o'tgach O'ZI qaytib ko'rinadi va boshqa ishchi uni
/// BOSHIDAN boshlaydi.
///
/// ── ESKIRGAN IJARA — QULAGAN ISHCHI, "DAVOM ETTIRISH" EMAS ──────────────
///
/// 🔴 YARIM QOLGAN ffmpeg NATIJASI HECH QACHON DAVOM ETTIRILMAYDI. Yarim
/// yozilgan mp4 da <c>moov</c> atomi yo'q; unga qo'shib yozilgan fayl uch
/// soniya o'ynab to'xtaydi, ya'ni faylsizlikdan HAM YOMON. Shuning uchun
/// egallash "ishni davom ettirish" emas, "ishni boshidan boshlash" degani
/// va u HAQIQIY urinish sifatida sanaladi.
///
/// ── ENG ESKISI BIRINCHI ─────────────────────────────────────────────────
///
/// Navbat <c>CreatedAt</c> bo'yicha o'sish tartibida bo'shatiladi. Bu
/// shunchaki adolat emas — SPEC ning oshkor talabi: tungi oynaga
/// sig'magan ish KEYINGI kechada, o'zidan keyin kelganlardan OLDIN
/// olinadi. Aks holda band kunlarda eng eski yozuv abadiy navbatning
/// oxirida qolardi.
/// </summary>
public interface IRecordingCompositionStore
{
    /// <summary>
    /// Navbatdagi (yoki ijarasi eskirgan) ENG ESKI yozuvni egallaydi.
    ///
    /// ⚠️ AMAL BITTA SQL BAYONOTI: tanlash va band qilish ajratilsa, ikki
    /// ishchi orasidagi mikrosoniyalik oraliqda ikkalasi ham "men oldim"
    /// deb qolardi. <c>FOR UPDATE SKIP LOCKED</c> esa ikkinchi ishchini
    /// KUTTIRMAYDI — u qulflangan qatorni o'tkazib yuboradi.
    /// </summary>
    /// <param name="lease">
    /// Ijara muddati. Ishlayotgan ishchi uni muntazam uzaytiradi
    /// (<see cref="RenewAsync"/>), shuning uchun bu qiymat "ish qancha
    /// davom etadi" emas, "ishchi qulaganini qancha vaqtda sezamiz"
    /// degani.
    /// </param>
    /// <returns>
    /// Egallangan qator yoki <c>null</c> — navbat bo'sh.
    /// </returns>
    Task<CompositionClaim?> ClaimAsync(TimeSpan lease, CancellationToken ct = default);

    /// <summary>
    /// Ijarani uzaytiradi (ffmpeg ishlayotganda har daqiqada).
    ///
    /// ★ NIMA UCHUN ALOHIDA, KICHIK AMAL: uzaytirish kodlash bilan
    /// PARALLEL bajariladi. Uni yozuv obyektini kuzatib turgan
    /// <c>DbContext</c> orqali qilish o'sha kontekstni ikki vazifa
    /// o'rtasida bo'lishishga majbur qilardi.
    /// </summary>
    /// <param name="claimedAt">
    /// <see cref="CompositionClaim.ClaimedAt"/> — EGALIK CHIPTASI.
    ///
    /// 🔴 BUSIZ UZAYTIRISH XAVFLI BO'LARDI: ishchi besh daqiqadan uzoq
    /// qotib qolsa (masalan diskda joy tugab), ijara eskiradi va boshqa
    /// ishchi qatorni oladi. Uyg'ongan birinchi ishchi esa uzaytirishni
    /// davom ettirib, qatorni O'ZIGA QAYTARIB OLARDI — ikkalasi ham
    /// yozar, ikkalasi ham AYNI kalitga.
    ///
    /// Chipta — egallash paytida yozilgan <c>CompositionStartedAt</c>:
    /// yangi egallash uni ALBATTA qayta yozadi, ya'ni eski qiymat bilan
    /// keladigan uzaytirish hech narsa topmaydi.
    /// </param>
    /// <returns>
    /// <c>false</c> — ijara BIZDA EMAS (boshqa ishchi egallab olgan yoki
    /// qator yakunlangan). Chaqiruvchi bunda ishni to'xtatishi kerak.
    /// </returns>
    Task<bool> RenewAsync(
        long recordingId, DateTimeOffset claimedAt, TimeSpan lease, CancellationToken ct = default);
}

/// <summary>
/// Egallangan qator.
/// </summary>
/// <param name="RecordingId">Yozuvning identifikatori.</param>
/// <param name="TookOverExpiredLease">
/// Bu QULAGAN ISHCHIDAN qolgan ishmi.
///
/// 🔴 NIMA UCHUN BU BAYROQ KERAK: shunday bo'lsa, boshlashdan OLDIN
/// oldingi urinishning izlari o'chirilishi SHART — ishchi papka ham,
/// YAKUNIY kalitdagi obyekt ham. Yarim yozilgan mp4 ustiga yozish
/// (yoki uni "topib olish") faylsizlikdan yomonroq natija berardi.
/// </param>
/// <param name="ClaimedAt">
/// Egallash payti — BAZA saqlagan ko'rinishida (mikrosekundgacha
/// yaxlitlangan). Ijarani uzaytirishda EGALIK CHIPTASI bo'lib xizmat
/// qiladi, shuning uchun u .NET dagi qiymat emas, aynan bazadan
/// QAYTARILGAN qiymat: aks holda solishtiruv aniqlik farqi tufayli
/// hech qachon mos kelmasdi.
/// </param>
public sealed record CompositionClaim(
    long RecordingId, bool TookOverExpiredLease, DateTimeOffset ClaimedAt);
