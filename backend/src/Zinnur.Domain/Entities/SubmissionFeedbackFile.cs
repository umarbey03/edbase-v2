using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Entities;

/// <summary>
/// ========================================================================
/// USTOZ TEKSHIRISHDA BIRIKTIRGAN FAYL (R37) — JAVOBGA "TESKARI" YO'NALISH
/// ========================================================================
///
/// Talab: *"student uchun ham teacher uchun ham vazifada fayl va rasm
/// jo'natish mumkin bo'lsin"*. O'quvchi tomoni ALLAQACHON ishlaydi
/// (<see cref="SubmissionFile"/>), ustoz tomoni esa umuman yo'q edi:
/// <c>GradeSubmissionRequest</c> — sof JSON (<c>Score</c> + <c>Feedback</c>).
///
/// ── 🔴 QAROR: <see cref="SubmissionFile"/> GA "YO'NALISH" USTUNI EMAS ───
///
/// Ikkita yo'l bor edi:
///
///   A) <c>SubmissionFile</c> ga <c>Direction</c> (yoki <c>AuthorId</c>)
///      ustuni qo'shish — bitta jadval, ikki ma'no.
///   B) ALOHIDA jadval (shu sinf).
///
/// TANLANDI: B. Sabablari, muhimidan boshlab:
///
///  1) ★ MAVJUD O'QISH YO'LLARINING HAMMASI "har qator — O'QUVCHINIKI"
///     degan taxminga QURILGAN va bu taxmin HECH QAYERDA yozilmagan:
///     <c>AssignmentService.OpenFileAsync</c> faylning EGASINI
///     <c>f.Submission!.StudentId</c> dan chiqaradi. Yo'nalish ustuni
///     qo'shilsa, o'sha qator ustozning faylini ham "o'quvchi yozgan"
///     deb hisoblardi va ruxsat qoidasi jimgina noto'g'ri bo'lardi.
///     Har o'qish joyiga <c>WHERE Direction = Student</c> qo'shish kerak
///     bo'lardi — bittasini unutish esa AYNAN loyihaning
///     <see cref="AssignmentAttachment"/> izohida ogohlantirilgan xato
///     ("bir jadvalda bo'lsa bitta WHERE ni unutish begona bolaning
///     ishini oshkor qilardi").
///
///  2) ★ <c>Submission.Files</c> NAVIGATSIYASI MA'NOSINI YO'QOTARDI. U
///     ikki joyda BIZNES qarori uchun ishlatiladi: topshirilgan javob
///     formatini tekshirishda (<c>AnswerFormats</c>) va
///     <see cref="Submission.MaxAttachments"/> sanog'ida. Ustozning PDF'i
///     shu kolleksiyaga tushsa, o'quvchi "5 ta fayl chegarasi"ga ustozning
///     fayllari tufayli yetib qolardi.
///
///  3) ★ QAYTA TOPSHIRISH SEMANTIKASI BOSHQA. <see cref="Submission.Resubmit"/>
///     bahoni va izohni TOZALAYDI (eski baho endi haqiqiy emas). Ustozning
///     tekshiruv fayli — o'sha ESKI bahoning bir qismi, o'quvchining yangi
///     javobining emas. Bitta jadvalda bu farqni ifodalash uchun yana bitta
///     ustun (<c>AttemptNumber</c>) kerak bo'lardi.
///
///  4) ★ LOYIHADA SHU AYNI QAROR ALLAQACHON BIR MARTA QABUL QILINGAN:
///     vazifa SHARTI (<see cref="AssignmentAttachment"/>) va vazifa JAVOBI
///     (<see cref="SubmissionFile"/>) ham "ikkalasi ham fayl" bo'lishiga
///     qaramay ALOHIDA jadval. Uchinchisini boshqa naqsh bilan qilish
///     kelajakdagi o'quvchini chalg'itardi.
///
/// ⚠️ NARXI: uchinchi "fayl" jadvali va uchinchi yuklash yo'li. Qabul
/// qilingan, chunki YUKLASH MEXANIZMI qayta ishlatiladi
/// (<c>IMediaStorage</c> + <c>MediaSignatures</c> + <c>RangeHeader</c>) —
/// takrorlanadigan narsa faqat ruxsat darvozasi, u esa har holda BOSHQA.
///
/// ── KIM KO'RADI ────────────────────────────────────────────────────────
///
/// Javobning EGASI (o'quvchi) VA o'sha o'quvchiga mas'ul xodim — ya'ni
/// <c>AssignmentService.EnsureCanReadStudentWorkAsync</c> bilan AYNI
/// qoida. Kim YOZADI: faqat baholay oladigan xodim.
/// </summary>
public class SubmissionFeedbackFile : BaseEntity
{
    /// <summary>
    /// Bitta javob tekshiruviga ko'pi bilan shuncha fayl —
    /// <see cref="Submission.MaxAttachments"/> bilan AYNI raqam
    /// (ikki tomon uchun bir xil qoida tushuntirishga oson).
    /// </summary>
    public const int MaxPerSubmission = Submission.MaxAttachments;

    /// <summary>Ko'rinadigan fayl nomi ustunining chegarasi.</summary>
    public const int MaxFileNameLength = GroupChatAttachment.MaxFileNameLength;

    public long SubmissionId { get; set; }

    public Submission? Submission { get; set; }

    /// <summary>Fayl turi — MAZMUNDAN aniqlanadi, klient aytganidan emas.</summary>
    public AttachmentKind Kind { get; set; }

    /// <summary>🔴 OMBOR KALITI — UI'GA CHIQMAYDI.</summary>
    public required string ObjectKey { get; set; }

    /// <summary>MAZMUNDAN aniqlangan MIME turi.</summary>
    public required string ContentType { get; set; }

    /// <summary>
    /// Ko'rinadigan nom (tozalangan). Ustoz "tuzatilgan-varaq.pdf" deb
    /// biriktirsa, o'quvchi AYNAN shu nomni ko'rishi kerak.
    /// </summary>
    public string? FileName { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>
    /// KIM biriktirgani — ustoz/kurator Id'si.
    ///
    /// ★ NIMA UCHUN SAQLANADI, <c>Submission.GradedById</c> DAN
    /// OLINMAYDI: baho keyinchalik BOSHQA xodim (o'quv bo'limi) tomonidan
    /// tuzatilishi mumkin, o'shanda <c>GradedById</c> o'zgaradi — lekin
    /// faylni kim qo'yganini bu o'zgartirmasligi kerak.
    /// </summary>
    public long? CreatedById { get; set; }

    // ---------------------------------------------------------------- xatti-harakat

    public void Validate()
    {
        if (SubmissionId <= 0)
            throw new DomainException("Fayl javobga bog'langan bo'lishi kerak.");

        if (string.IsNullOrWhiteSpace(ObjectKey))
            throw new DomainException("Ombor kaliti bo'sh bo'lishi mumkin emas.");

        if (string.IsNullOrWhiteSpace(ContentType))
            throw new DomainException("Fayl turi (MIME) aniqlanmagan.");

        if (SizeBytes <= 0)
            throw new DomainException("Fayl hajmi noldan katta bo'lishi kerak.");
    }
}
