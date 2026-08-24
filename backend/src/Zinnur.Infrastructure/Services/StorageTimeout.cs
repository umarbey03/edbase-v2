namespace Zinnur.Infrastructure.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// OMBOR AMALLARI UCHUN TIMEOUT — HAR AMALGA O'ZINIKI
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN BU SINF PAYDO BO'LDI (2026-08-24)
///
/// Ilgari timeout <c>HttpClient.Timeout</c> da turardi. U esa KLIENTGA
/// tegishli, AMALGA emas — bitta nomlangan klientni uchala ombor xizmati
/// bo'lishadi (`R2SubmissionStorage`, `R2MediaStorage`,
/// `R2RecordingStorage`), ularning ehtiyoji esa butunlay boshqa:
///
///   HEAD / DELETE / sarlavha olish -> soniyalar
///   vazifa javobi (≤10 MB)         -> o'nlab soniyalar
///   dars videosi (2 GB gacha)      -> o'nlab DAQIQALAR
///
/// Yagona qiymat bilan bu uchtasini qondirib bo'lmasdi: 60 s katta
/// videoni o'ldirardi (batafsil arifmetika:
/// <c>StorageOptions.LargeUploadTimeoutSeconds</c>), 1800 s esa osilib
/// qolgan HEAD so'rovini yarim soat ushlab turardi.
///
/// ⚠️ .NET da <c>HttpClient.Timeout</c> ni BITTA so'rov uchun
/// o'zgartirib bo'lmaydi — shuning uchun klientda u
/// <c>InfiniteTimeSpan</c> ga qo'yildi va chegara SHU YERDA, bog'langan
/// (linked) token bilan beriladi. Qoida: <c>zinnur-storage</c> klienti
/// orqali yuborilgan HAR BIR so'rov shu sinfdan o'tishi SHART — aks
/// holda u umuman chegarasiz qoladi.
///
/// ★★ NEGA <c>using</c> BILAN TASHLASH OQIMNI BUZMAYDI: katta faylni
/// o'qish yo'li (<c>ResponseHeadersRead</c>) javob TANASINI keyinroq,
/// kontroller uzatayotganda o'qiydi. <c>CancellationTokenSource.Dispose</c>
/// tokenni BEKOR QILMAYDI — u faqat ichki taymerni to'xtatadi. Ya'ni
/// sarlavha olingach chegara olib tashlanadi va video oqimi o'z yo'lida
/// davom etadi; uni endi faqat foydalanuvchining uzilishi (<c>ct</c>)
/// yoki nginx to'xtatadi. Aynan shu kerak: 40 daqiqalik darsni "ombor
/// timeout'i" uzib qo'ymasin.
/// </summary>
internal static class StorageTimeout
{
    /// <summary>
    /// Eng past chegara. Undan kichik qiymat sozlansa amal boshlanmasdan
    /// uzilardi va sabab "ombor javob bermadi" ko'rinishida chiqardi.
    /// </summary>
    private const int MinSeconds = 5;

    /// <summary>
    /// Eng yuqori chegara (1 soat). Yuqorida — nginx'ning
    /// <c>proxy_read_timeout 3600s</c> qiymati; undan oshirish ma'nosiz,
    /// chunki so'rov baribir nginx'da uzilardi.
    /// </summary>
    private const int MaxSeconds = 3600;

    /// <summary>
    /// Chaqiruvchining tokeni bilan BOG'LANGAN, muddatli manba yasaydi.
    ///
    /// ⚠️ NEGA BOG'LANGAN: foydalanuvchi sahifani yopsa
    /// (<c>HttpContext.RequestAborted</c>) yuklash DARHOL to'xtashi
    /// kerak — chegaraning tugashini kutib o'tirmasdan.
    ///
    /// ★ Natijada chaqiruvchidagi
    /// <c>catch (TaskCanceledException) when (!ct.IsCancellationRequested)</c>
    /// namunasi AVVALGIDEK ishlaydi: bizning chegaramiz tugasa
    /// chaqiruvchining <c>ct</c> si bekor qilinmagan bo'ladi, ya'ni
    /// "timeout" bilan "foydalanuvchi bekor qildi" bir-biridan
    /// ajratiladi.
    /// </summary>
    internal static CancellationTokenSource Start(int seconds, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(seconds, MinSeconds, MaxSeconds)));

        return cts;
    }
}
