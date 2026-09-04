using Zinnur.Application.Recordings.Dtos;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TREK QUVURINING WEBHOOK ISHLOVCHISI (SPEC-RECORDING-V2 §3.3)
/// ════════════════════════════════════════════════════════════════════════
///
/// LiveKit hodisasini <c>RecordingPipeline.TrackComposition</c> yozuvlari
/// nuqtai nazaridan qayta ishlaydi: xona ovozi mikserini va har bir
/// trek egress'ini AYNAN shu yerdan boshlaydi, to'xtatadi va yakunlaydi.
///
/// ── NIMA UCHUN ALOHIDA PORT, <see cref="IRecordingWebhookHandler"/> GA
///    QO'SHIMCHA METOD EMAS ────────────────────────────────────────────────
///
/// Eski ishlovchi <c>egress_id</c> bo'lmagan HAR QANDAY hodisani
/// <c>Ignored</c> deb qaytaradi va u ESKI QUVURNING JONLI YO'LI — ya'ni
/// uning xatti-harakati bitta baytga ham o'zgarmasligi kerak
/// (SPEC-RECORDING-V2 §5.9: unga tegish RUXSAT ETILMAGAN o'zgarishlar
/// ro'yxatida). Ikkinchi mas'uliyatni o'sha sinfga qo'shish esa
/// "eski quvurni buzmasdan yangisini qo'shish" degan butun rejani
/// bekor qilardi.
///
/// ── 🔴 SHARTNOMANING ENG MUHIM BANDI: <c>Ignored</c> = "MENIKI EMAS" ────
///
/// Controller ikkala ishlovchini KETMA-KET chaqiradi: avval shu port,
/// keyin — faqat <c>Ignored</c> qaytganda — eski ishlovchi
/// (<c>LiveKitWebhookController</c>). Shundan kelib chiqadigan IKKI
/// QAT'IY qoida:
///
///  1) <c>Ignored</c> qaytariladigan yo'lda
///     <see cref="ILiveKitWebhookLog.TryBeginAsync"/> CHAQIRILMAYDI.
///     🔴 Sabab: u hodisani AYNI <c>DbContext</c> kuzatuvchisiga
///     "ishlangan" deb qo'shadi. Chaqirib turib <c>Ignored</c> qaytarsak,
///     eski ishlovchi o'z navbatida takror deb ko'rardi va ESKI QUVURNING
///     <c>egress_ended</c> hodisasi JIMGINA yo'qolardi — ya'ni dars
///     yozuvi abadiy "Active" bo'lib qolardi. Umumiy qoida ham shu
///     (§3.3): arzon filtrlar avval, takror jurnali esa faqat holat
///     o'zgarishidan OLDIN.
///
///  2) Hodisa BIZNIKI deb tanilgach, <c>Ignored</c> QAYTARILMAYDI —
///     hech narsa o'zgarmagan bo'lsa ham
///     (<see cref="RecordingWebhookOutcome.Handled"/>). Aks holda har
///     bir oraliq trek hodisasi eski ishlovchiga tushib, "noma'lum
///     egress" ogohlantirishini yozardi.
///
/// ⚠️ BU ISHLOVCHI ISTISNO EMAS, TASHQI CHAQIRUV QILADI — va bu
/// eski ishlovchidan ONGLI FARQ: trek egress'i webhook ichida, DARHOL
/// boshlanadi. Sabab SPEC-RECORDING-V2 §3.3 da: navbatdagi vazifa eng
/// yaxshi holatda 30 soniya kechikadi va o'sha 30 soniya HAR ekran
/// ulashishning boshidan qirqilardi. Webhook esa hech kimni
/// kutdirmaydi: controller javobni darhol 200 qiladi va Twirp mijozining
/// o'z muhlati (10 s) bor.
/// </summary>
public interface ITrackRecordingWebhookHandler
{
    /// <summary>
    /// Hodisani ishlaydi.
    /// </summary>
    /// <param name="body">So'rov tanasi — AYNAN kelgan baytlar.</param>
    /// <returns>
    /// <see cref="RecordingWebhookOutcome.Ignored"/> — hodisa BU quvurga
    /// tegishli emas, chaqiruvchi uni eski ishlovchiga uzatishi kerak.
    /// Boshqa har qanday qiymat — hodisa SHU YERDA yakunlandi.
    /// </returns>
    Task<RecordingWebhookOutcome> HandleAsync(
        ReadOnlyMemory<byte> body, CancellationToken ct = default);
}
