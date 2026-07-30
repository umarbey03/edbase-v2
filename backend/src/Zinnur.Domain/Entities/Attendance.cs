using Zinnur.Domain.Common;
using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Entities;

/// <summary>
/// O'quvchining bitta jonli darsdagi davomati.
/// (SessionId, StudentId) — UNIKAL.
/// </summary>
public class Attendance : BaseEntity
{
    /// <summary>Shu vaqtdan kam qatnashgan "Partial" hisoblanadi.</summary>
    public const int MinFullAttendanceSeconds = 15 * 60;

    public long SessionId { get; set; }

    public LiveSession? Session { get; set; }

    public long StudentId { get; set; }

    public User? Student { get; set; }

    public AttendanceStatus Status { get; set; } = AttendanceStatus.Absent;

    /// <summary>BIRINCHI kirish — tarix uchun, hech qachon o'zgarmaydi.</summary>
    public DateTimeOffset? FirstJoinAt { get; set; }

    /// <summary>
    /// OXIRGI kirish — har ulanishda yangilanadi. Davomiylik SHUNDAN hisoblanadi.
    ///
    /// NIMA UCHUN IKKITA MAYDON: eski tizimda faqat bitta `joined_at` bor edi va
    /// u faqat birinchi kirishda yozilardi, chiqishda esa
    /// `duration += now - joined_at` qilinardi. Natijada zaif internetda
    /// qayta ulangan o'quvchining vaqti har safar dars boshidan qayta
    /// qo'shilardi: 80 daqiqalik darsda 125 daqiqa chiqishi mumkin edi.
    /// </summary>
    public DateTimeOffset? LastJoinAt { get; set; }

    public DateTimeOffset? LeftAt { get; set; }

    /// <summary>Faqat YAKUNLANGAN seanslar yig'indisi.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Qo'lda o'zgartirilganmi (ustoz/o'quv bo'limi).</summary>
    public bool IsManual { get; set; }

    /// <summary>
    /// Qo'lda tuzatish SABABI — "interneti uzildi", "kasal", "kechikib
    /// keldi". Faqat <see cref="IsManual"/> bo'lganda ma'noli.
    ///
    /// NIMA UCHUN AUDITDA EMAS, SHU YERDA HAM: bu joriy holatning
    /// TUSHUNTIRISHI va davomat jadvalidagi katakda ko'rsatiladi. Audit
    /// esa TARIX — undan "hozirgi sabab nima" degan savolga javob olish
    /// har katak uchun alohida so'rov talab qilardi (jadvalda 30 o'quvchi
    /// × 70 dars).
    /// </summary>
    public string? Reason { get; set; }

    // ---------------------------------------------------------------- xatti-harakat

    /// <summary>
    /// ★ QO'LDA TUZATISH (ustoz / kurator / o'quv bo'limi).
    ///
    /// FAQAT bahoni va sababni o'zgartiradi. Vaqt maydonlariga
    /// (<see cref="FirstJoinAt"/>, <see cref="LastJoinAt"/>,
    /// <see cref="LeftAt"/>, <see cref="DurationSeconds"/>) UMUMAN
    /// TEGMAYDI.
    ///
    /// NIMA UCHUN TEGMAYDI: bu maydonlar SignalR hodisalarining o'lchov
    /// natijasi va <see cref="RegisterJoin"/>/<see cref="RegisterLeave"/>
    /// juftligi ular ustida invariant saqlaydi ("qayta ulanish vaqtni ikki
    /// barobar qilmaydi"). Qo'lda tuzatish ular ichiga yozsa, o'quvchi
    /// hozir XONADA bo'lgan holatda ochiq seans buzilib, dars oxiridagi
    /// <see cref="Finalize"/> vaqtni noto'g'ri qo'shardi. Ustoz "kelgan"
    /// deb belgilaydi — o'lchov esa o'zi bo'lganicha qoladi va
    /// <see cref="IsManual"/> bayrog'i "bu son emas, QAROR" deb aytadi.
    ///
    /// Shu tufayli tuzatishni dars DAVOM ETAYOTGANDA ham qilish xavfsiz:
    /// keyingi <see cref="RegisterJoin"/> ham, yakuniy
    /// <see cref="Finalize"/> ham <see cref="IsManual"/> tufayli bahoni
    /// qayta yozmaydi.
    /// </summary>
    /// <param name="status">Yangi baho.</param>
    /// <param name="reason">Sabab (ixtiyoriy). <c>null</c> — sabab tozalanadi.</param>
    /// <param name="now">Amal vaqti (UTC).</param>
    public void ApplyManual(AttendanceStatus status, string? reason, DateTimeOffset now)
    {
        Status = status;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        IsManual = true;
        UpdatedAt = now;
    }

    /// <summary>O'quvchi xonaga kirdi (birinchi marta yoki qayta ulandi).</summary>
    public void RegisterJoin(DateTimeOffset now)
    {
        FirstJoinAt ??= now;      // tarix — o'zgarmaydi
        LastJoinAt = now;         // joriy seans boshlanishi
        LeftAt = null;
        UpdatedAt = now;

        // QO'LDA QO'YILGAN BAHOGA TEGILMAYDI.
        //
        // Sabab: ilgari bu shart yo'q edi va assimetriya tuzoq hosil qilardi —
        // ustoz o'quvchini qo'lda "Absent" deb belgilaydi (IsManual=true), keyin
        // o'quvchi qayta ulanadi va status jimgina "Present" ga o'zgaradi.
        // `Finalize()` esa AYNAN IsManual tufayli qayta hisoblamaydi, natijada
        // noto'g'ri "Present" abadiy qolib ketardi — `IsManual` bayrog'i aynan
        // shundan himoya qilish uchun mavjud.
        if (IsManual) return;

        if (Status == AttendanceStatus.Absent)
            Status = AttendanceStatus.Present;
    }

    /// <summary>O'quvchi xonadan chiqdi — SHU seans vaqti qo'shiladi.</summary>
    public void RegisterLeave(DateTimeOffset now)
    {
        if (LastJoinAt is { } joined && now > joined)
            DurationSeconds += (int)(now - joined).TotalSeconds;

        LastJoinAt = null;        // seans yopildi — ikki marta qo'shilmaydi
        LeftAt = now;
        UpdatedAt = now;
    }

    /// <summary>Dars yakunlanganda: ochiq seansni yopadi va yakuniy holatni qo'yadi.</summary>
    public void Finalize(DateTimeOffset now)
    {
        if (LastJoinAt is not null)
            RegisterLeave(now);   // xonadan chiqmasdan dars tugagan

        if (IsManual) return;     // qo'lda qo'yilgan bahoga tegmaymiz

        Status = DurationSeconds switch
        {
            0 => AttendanceStatus.Absent,
            < MinFullAttendanceSeconds => AttendanceStatus.Partial,
            _ => AttendanceStatus.Present,
        };

        UpdatedAt = now;
    }
}
