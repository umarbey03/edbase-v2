namespace Zinnur.Application.Jobs;

/// <summary>
/// Vazifani QULF OSTIDA yurgizadi, vaqtini o'lchaydi va natijani logga
/// yozadi.
///
/// ★ KAFOLAT: bu metodlar (bekor qilishdan tashqari) HECH QACHON istisno
/// TASHLAMAYDI. Sabab: bitta vazifadagi xato ikkinchisini ham, butun fon
/// xizmatini ham to'xtatib qo'ymasligi kerak. Eski tizimda aynan shunday
/// bo'lardi — bitta vazifa yiqilsa rejalashtiruvchi jimgina o'lardi va
/// buni haftalar davomida hech kim sezmasdi.
/// </summary>
public interface IJobRunner
{
    /// <summary>Bitta vazifani yurgizadi (qulf + o'lchov + log).</summary>
    Task<JobExecution> RunAsync(IScheduledJob job, CancellationToken ct = default);

    /// <summary>
    /// Berilgan vazifalarni KETMA-KET yurgizadi. Biri yiqilsa qolganlari
    /// baribir bajariladi.
    ///
    /// ★ NIMA UCHUN KETMA-KET, PARALLEL EMAS: har vazifa o'z bazaviy
    /// scope'idagi <c>DbContext</c> ga tayanadi, u esa THREAD-SAFE EMAS.
    /// Vazifalar kam va qisqa, shuning uchun ketma-ketlik hech narsa
    /// yo'qotmaydi.
    /// </summary>
    Task<IReadOnlyList<JobExecution>> RunAllAsync(
        IEnumerable<IScheduledJob> jobs, CancellationToken ct = default);
}

/// <summary>Bitta yurishning yakuni.</summary>
/// <param name="Name">Vazifa nomi.</param>
/// <param name="Outcome">Nima bo'ldi.</param>
/// <param name="Result">Vazifaning o'z hisoboti (bajarilgan bo'lsa).</param>
/// <param name="Duration">Qancha vaqt ketdi.</param>
/// <param name="ErrorMessage">Yiqilgan bo'lsa — qisqa sabab.</param>
public sealed record JobExecution(
    string Name,
    JobOutcome Outcome,
    JobRunResult Result,
    TimeSpan Duration,
    string? ErrorMessage = null);

/// <summary>Yurish yakuni.</summary>
public enum JobOutcome
{
    /// <summary>Qulf olindi va vazifa oxirigacha bajarildi.</summary>
    Completed = 0,

    /// <summary>
    /// Qulf BOSHQA instance'da — o'tkazib yuborildi. Bu XATO EMAS, aksincha
    /// leader lock'ning ishlayotganining dalili.
    /// </summary>
    SkippedLocked = 1,

    /// <summary>Vazifa istisno bilan yiqildi. Keyingi yurishda qaytadan urinamiz.</summary>
    Failed = 2,
}
