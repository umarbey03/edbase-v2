namespace Zinnur.Application.Jobs;

/// <summary>
/// Vaqti-vaqti bilan bajariladigan fon vazifasi.
///
/// ★ NIMA UCHUN <c>BackgroundService</c> DAN AJRATILGAN: hosting (sikl,
/// uyqu, to'xtatish) — WebApi qatlamining ishi; "nima qilinadi va qanday
/// qoida bo'yicha" esa BIZNES mantiqi va u shu yerda — testda mock'siz,
/// haqiqiy baza bilan sinaladi. Testlar vazifani O'ZI chaqiradi va natijani
/// darhol tekshiradi, fon xizmatining uyquda kutishini kutmaydi
/// (<c>IOutboxDispatcher</c> bilan bir xil yondashuv).
///
/// ★ QULFNI VAZIFANING O'ZI OLMAYDI — buni <see cref="IJobRunner"/>
/// bajaradi. Aks holda har yangi vazifa qulflashni qaytadan yozardi va
/// bittasida albatta unutilardi.
/// </summary>
public interface IScheduledJob
{
    /// <summary>
    /// Barqaror texnik nom: qulf kaliti ham, log maydoni ham shundan
    /// olinadi. O'ZGARTIRILMASIN — o'zgarsa eski nom bilan qulf olgan
    /// instance va yangisi bir-birini KO'RMAY qoladi va vazifa ikki marta
    /// bajarilishi mumkin (yangilanish paytida ikki versiya yonma-yon
    /// ishlaydi).
    /// </summary>
    string Name { get; }

    /// <summary>Ikki yurish orasidagi eng qisqa masofa.</summary>
    TimeSpan Interval { get; }

    /// <summary>
    /// Bitta yurish.
    ///
    /// ★ IDEMPOTENT bo'lishi SHART: qulf ushlab turilgan bo'lsa ham,
    /// instance ish o'rtasida qulashi va vazifa qaytadan boshlanishi mumkin.
    /// </summary>
    Task<JobRunResult> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Bitta yurishning natijasi — LOG uchun. Xato holati bu yerda YO'Q:
/// yiqilish istisno bilan bildiriladi va uni <see cref="IJobRunner"/>
/// ushlaydi.
/// </summary>
/// <param name="Processed">Haqiqatan o'zgartirilgan yozuvlar soni.</param>
/// <param name="Skipped">Ko'rilgan, lekin ataylab tegilmagan yozuvlar soni.</param>
/// <param name="Note">Logga tushadigan qisqa izoh (masalan hisob oyi).</param>
public readonly record struct JobRunResult(int Processed, int Skipped, string? Note = null)
{
    /// <summary>"Ish topilmadi" — eng ko'p uchraydigan natija.</summary>
    public static JobRunResult Nothing => default;

    /// <summary>Umuman biror narsa qilindimi (log darajasini tanlash uchun).</summary>
    public bool HasWork => Processed > 0 || Skipped > 0;
}
