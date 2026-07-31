using System.Globalization;
using Zinnur.Domain.Enums;

namespace Zinnur.Migration.Mapping;

/// <summary>
/// ========================================================================
/// ESKI QIYMATLARNI v2 ENUM'LARIGA XARITALASH — HAR QIYMAT OSHKOR
/// ========================================================================
///
/// ★ NIMA UCHUN HAR BIRI QO'LDA YOZILGAN, TARTIBGA TAYANILMAGAN:
/// v2 enum'lari bazaga <c>int</c> sifatida yoziladi va ularning tartibi
/// eski satrlar tartibiga MOS EMAS. Masalan eski
/// <c>attendance_status</c> ENUM'i <c>('present','late','partial','absent')</c>
/// tartibida e'lon qilingan, v2 da esa <c>Absent = 0</c>. Agar bu yerda
/// "eski ENUM indeksini olib int qilamiz" deyilganda BARCHA davomat
/// bahosi almashib ketardi va buni hech bir FK yoki CHECK ushlamasdi.
///
/// ★ NOMA'LUM QIYMAT JIMGINA 0 GA TUSHMAYDI. Har xaritalash
/// <c>bool TryMap(...)</c> ko'rinishida: tanilmagan satr <c>false</c>
/// qaytaradi va chaqiruvchi qatorni O'TKAZIB YUBORIB, sababini hisobotga
/// yozadi. Standart qiymat berish "hammasi ko'chdi" degan yolg'on hisobot
/// yaratardi.
/// </summary>
internal static class LegacyMap
{
    // ====================================================================
    // HAFTA KUNI — ENG XAVFLI KONVERTATSIYA
    // ====================================================================

    /// <summary>
    /// Eski Python <c>date.weekday()</c> (Dushanba = 0 ... Yakshanba = 6)
    /// -> .NET <see cref="DayOfWeek"/> (Yakshanba = 0 ... Shanba = 6).
    ///
    /// ★ Formula: <c>dotnet = (python + 1) % 7</c>.
    /// Konvertatsiyasiz BARCHA guruhlarning dars kunlari bir kun oldinga
    /// siljib ketardi (dushanba guruhi yakshanbaga tushardi) va buni hech
    /// qanday cheklov ushlamasdi — jadval "to'g'ri" ko'rinib turardi.
    ///
    /// Tekshiruv <c>Reconciler</c> da: har dars sanasining Toshkent bo'yicha
    /// hafta kuni guruhning <c>Weekdays</c> ro'yxatida borligi TALAB qilinadi.
    /// </summary>
    public static bool TryWeekday(int pythonWeekday, out int dotnetWeekday)
    {
        if (pythonWeekday is < 0 or > 6)
        {
            dotnetWeekday = 0;
            return false;
        }

        dotnetWeekday = (pythonWeekday + 1) % 7;
        return true;
    }

    // ====================================================================
    // FOYDALANUVCHI ROLI
    // ====================================================================

    /// <summary>Eski <c>user_role</c> ENUM'i -> <see cref="UserRole"/>.</summary>
    public static bool TryRole(string? legacy, out UserRole role)
    {
        switch (Key(legacy))
        {
            case "student": role = UserRole.Student; return true;
            case "teacher": role = UserRole.Teacher; return true;
            case "assistant": role = UserRole.Assistant; return true;
            case "academic": role = UserRole.Academic; return true;
            case "admin": role = UserRole.Admin; return true;
            default: role = UserRole.Student; return false;
        }
    }

    // ====================================================================
    // GURUH
    // ====================================================================

    /// <summary>
    /// Eski <c>groups.group_type</c> (erkin matn) -> <see cref="GroupType"/>.
    ///
    /// Eski tizim kurator guruhini IKKI xil nom bilan yozgan:
    /// <c>"curator"</c> va <c>"assistant"</c> (qarang
    /// <c>academic_router._parse_weekdays</c> va <c>live_router</c>:
    /// <c>group_type in ("curator", "assistant")</c>). Ikkalasi ham
    /// <see cref="GroupType.Curator"/> ga tushadi.
    /// </summary>
    public static bool TryGroupType(string? legacy, out GroupType type)
    {
        switch (Key(legacy))
        {
            case "group": type = GroupType.Group; return true;
            case "individual": type = GroupType.Individual; return true;
            case "curator":
            case "assistant": type = GroupType.Curator; return true;
            default: type = GroupType.Group; return false;
        }
    }

    /// <summary>
    /// Eski <c>groups.status</c> (<c>active|archived</c>) -> <c>IsActive</c>.
    /// v2 da alohida "arxiv" holati YO'Q — faqat bayroq.
    /// </summary>
    public static bool TryGroupActive(string? legacy, out bool isActive)
    {
        switch (Key(legacy))
        {
            case "active": isActive = true; return true;
            case "archived":
            case "inactive": isActive = false; return true;
            default: isActive = false; return false;
        }
    }

    /// <summary>Eski <c>group_members.status</c> -> <see cref="MemberStatus"/>.</summary>
    public static bool TryMemberStatus(string? legacy, out MemberStatus status)
    {
        switch (Key(legacy))
        {
            case "active": status = MemberStatus.Active; return true;
            case "paused": status = MemberStatus.Paused; return true;
            case "stopped": status = MemberStatus.Stopped; return true;
            case "moved": status = MemberStatus.Moved; return true;
            default: status = MemberStatus.Active; return false;
        }
    }

    // ====================================================================
    // JONLI DARS VA DAVOMAT
    // ====================================================================

    /// <summary>Eski <c>lesson_type</c> -> <see cref="SessionType"/>.</summary>
    public static bool TrySessionType(string? legacy, out SessionType type)
    {
        switch (Key(legacy))
        {
            case "teacher": type = SessionType.Teacher; return true;
            case "assistant": type = SessionType.Assistant; return true;
            default: type = SessionType.Teacher; return false;
        }
    }

    /// <summary>Eski <c>lesson_status</c> -> <see cref="SessionStatus"/>.</summary>
    public static bool TrySessionStatus(string? legacy, out SessionStatus status)
    {
        switch (Key(legacy))
        {
            case "scheduled": status = SessionStatus.Scheduled; return true;
            case "live": status = SessionStatus.Live; return true;
            case "ended": status = SessionStatus.Ended; return true;
            case "cancelled": status = SessionStatus.Cancelled; return true;
            default: status = SessionStatus.Scheduled; return false;
        }
    }

    /// <summary>
    /// Eski <c>attendance_status</c> -> <see cref="AttendanceStatus"/>.
    ///
    /// ★ Eski ENUM tartibi <c>('present','late','partial','absent')</c>,
    /// v2 da esa <c>Absent = 0, Present = 1, Late = 2, Partial = 3</c>.
    /// Ya'ni tartib MOS EMAS — shuning uchun nom bo'yicha xaritalanadi.
    /// </summary>
    public static bool TryAttendanceStatus(string? legacy, out AttendanceStatus status)
    {
        switch (Key(legacy))
        {
            case "absent": status = AttendanceStatus.Absent; return true;
            case "present": status = AttendanceStatus.Present; return true;
            case "late": status = AttendanceStatus.Late; return true;
            case "partial": status = AttendanceStatus.Partial; return true;
            default: status = AttendanceStatus.Absent; return false;
        }
    }

    // ====================================================================
    // GURUH CHATI — IKKI OQIM
    // ====================================================================

    /// <summary>
    /// Eski <c>chat_messages.channel</c> -> <see cref="GroupChatChannel"/>.
    ///
    /// ★ ENG JIM ZARAR SHU YERDA: eski ilovada o'quvchi USTOZGA va
    /// KURATORGA alohida yozadi (<c>student_router._norm_channel</c>).
    /// Kanal tashlab yuborilsa ikki oqim qo'shilib ketadi va ustoz
    /// o'quvchining kuratorga atalgan savollarini o'qib qoladi — bunday
    /// xatoni na FK, na CHECK ushlamaydi.
    ///
    /// Eski ilovaning O'ZI ham "assistant bo'lmasa teacher" qoidasini
    /// ishlatadi, shuning uchun bu yerda ham NOMA'LUM qiymat xato emas:
    /// <c>Teacher</c> ga tushadi, lekin hisobotda alohida sanaladi.
    /// </summary>
    public static GroupChatChannel Channel(string? legacy, out bool wasKnown)
    {
        switch (Key(legacy))
        {
            case "assistant":
            case "curator":
                wasKnown = true;
                return GroupChatChannel.Curator;
            case "teacher":
                wasKnown = true;
                return GroupChatChannel.Teacher;
            default:
                wasKnown = false;
                return GroupChatChannel.Teacher;
        }
    }

    // ====================================================================
    // O'QUV JARAYONI
    // ====================================================================

    /// <summary>Eski <c>submissions.status</c> -> <see cref="SubmissionStatus"/>.</summary>
    public static bool TrySubmissionStatus(string? legacy, out SubmissionStatus status)
    {
        switch (Key(legacy))
        {
            case "submitted": status = SubmissionStatus.Submitted; return true;
            case "graded": status = SubmissionStatus.Graded; return true;
            default: status = SubmissionStatus.Submitted; return false;
        }
    }

    /// <summary>Eski <c>test_attempts.status</c> -> <see cref="AttemptStatus"/>.</summary>
    public static bool TryAttemptStatus(string? legacy, out AttemptStatus status)
    {
        switch (Key(legacy))
        {
            case "in_progress": status = AttemptStatus.InProgress; return true;
            case "submitted": status = AttemptStatus.Submitted; return true;
            default: status = AttemptStatus.InProgress; return false;
        }
    }

    /// <summary>Eski <c>tests.kind</c> -> <see cref="TestKind"/>.</summary>
    public static bool TryTestKind(string? legacy, out TestKind kind)
    {
        switch (Key(legacy))
        {
            case "lesson": kind = TestKind.Lesson; return true;
            case "competition": kind = TestKind.Competition; return true;
            default: kind = TestKind.Competition; return false;
        }
    }

    /// <summary>Eski <c>submission_files.kind</c> -> <see cref="AttachmentKind"/>.</summary>
    public static AttachmentKind AttachmentKind(string? legacy) => Key(legacy) switch
    {
        "image" => Domain.Enums.AttachmentKind.Image,
        "audio" => Domain.Enums.AttachmentKind.Audio,
        _ => Domain.Enums.AttachmentKind.Document,
    };

    /// <summary>
    /// Eski <c>assignments.answer_formats</c> (CSV: <c>"text,image,audio"</c>)
    /// -> <see cref="AnswerFormats"/> bayroqlari.
    ///
    /// Bo'sh yoki tanilmagan bo'lsa eski standart qiymat qo'llanadi
    /// (<c>text,image</c> — <c>models.py</c> dagi <c>default</c>), aks holda
    /// o'quvchi javob yubora olmaydigan vazifa hosil bo'lardi.
    /// </summary>
    public static AnswerFormats Formats(string? csv, out bool wasComplete)
    {
        wasComplete = true;
        var result = AnswerFormats.None;

        foreach (var part in (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "text": result |= AnswerFormats.Text; break;
                case "image": result |= AnswerFormats.Image; break;
                case "audio": result |= AnswerFormats.Audio; break;
                default: wasComplete = false; break;
            }
        }

        if (result != AnswerFormats.None) return result;

        wasComplete = false;
        return AnswerFormats.Text | AnswerFormats.Image;
    }

    // ====================================================================
    // MOLIYA
    // ====================================================================

    /// <summary>Eski <c>payments.status</c> -> <see cref="PaymentStatus"/>.</summary>
    public static bool TryPaymentStatus(string? legacy, out PaymentStatus status)
    {
        switch (Key(legacy))
        {
            case "due": status = PaymentStatus.Due; return true;
            case "partial": status = PaymentStatus.Partial; return true;
            case "paid": status = PaymentStatus.Paid; return true;
            case "waived": status = PaymentStatus.Waived; return true;
            default: status = PaymentStatus.Due; return false;
        }
    }

    /// <summary>Eski <c>student_discounts.kind</c> -> <see cref="DiscountKind"/>.</summary>
    public static bool TryDiscountKind(string? legacy, out DiscountKind kind)
    {
        switch (Key(legacy))
        {
            case "percent": kind = DiscountKind.Percent; return true;
            case "fixed":
            case "amount": kind = DiscountKind.Amount; return true;
            default: kind = DiscountKind.Percent; return false;
        }
    }

    /// <summary>
    /// Eski <c>payment_transactions.type</c> -> <see cref="PaymentTransactionKind"/>.
    ///
    /// ★ <c>"due"</c> QIYMATI KO'CHIRILMAYDI (<c>false</c> qaytadi):
    /// u pul HARAKATI emas, oy ochilgani haqidagi belgi. v2 da bu holat
    /// <c>Payments.Status = Due</c> qatorining O'ZIDA turadi. Jurnalga
    /// yozilsa kunlik tushum hisoboti bo'lmagan pulni ko'rsatardi.
    ///
    /// <c>"lesson_deduct"</c> (dars yechish) -> <c>BalanceUse</c>,
    /// <c>"lesson_refund"</c> (balans tiklash) -> <c>Refund</c>:
    /// ikkalasi ham <c>academic_router</c> da balans bilan ishlaydi.
    /// </summary>
    public static bool TryTransactionKind(string? legacy, out PaymentTransactionKind kind)
    {
        switch (Key(legacy))
        {
            case "paid": kind = PaymentTransactionKind.Payment; return true;
            case "refund":
            case "lesson_refund": kind = PaymentTransactionKind.Refund; return true;
            case "waived": kind = PaymentTransactionKind.Waiver; return true;
            case "lesson_deduct": kind = PaymentTransactionKind.BalanceUse; return true;
            default: kind = PaymentTransactionKind.Payment; return false;
        }
    }

    /// <summary>
    /// Eski ERKIN SATR to'lov usuli -> <see cref="PaymentMethod"/>.
    ///
    /// ★ v2 da ATAYLAB IKKITA qiymat bor (<c>Cash</c>, <c>Card</c>) —
    /// sabab <c>PaymentMethod</c> izohida. Onlayn to'lov tizimlari
    /// (Click, Payme, Uzum) kassaga NAQD tushmaydi, ya'ni ular
    /// <c>Card</c> guruhiga kiradi.
    ///
    /// TANILMAGAN satr <c>null</c> beradi (<c>wasKnown = false</c>) va
    /// ASL MATN izohga qo'shiladi — shunda ma'lumot yo'qolmaydi va
    /// hisobotda ko'rinadi.
    /// </summary>
    public static PaymentMethod? Method(string? legacy, out bool wasKnown)
    {
        wasKnown = true;

        switch (Key(legacy))
        {
            case "":
                return null;                                  // bo'sh — pul tushmagan

            case "naqd":
            case "naqd pul":
            case "cash":
                return PaymentMethod.Cash;

            case "karta":
            case "karta orqali":
            case "card":
            case "plastik":
            case "plastik karta":
            case "uzcard":
            case "humo":
            case "click":
            case "payme":
            case "uzum":
            case "bank":
            case "o'tkazma":
            case "otkazma":
            case "perevod":
                return PaymentMethod.Card;

            case "balans_tiklash":
            case "dars_yechish":
                // Ichki balans amali — kassaga pul tushmagan, usul YO'Q.
                return null;

            default:
                wasKnown = false;
                return null;
        }
    }

    // ====================================================================
    // YORDAMCHILAR
    // ====================================================================

    /// <summary>
    /// Taqqoslash uchun kalit: bo'shliq kesiladi va kichik harfga o'tadi.
    /// <see cref="CultureInfo.InvariantCulture"/> MAJBURIY — turk lokalida
    /// <c>"I".ToLower()</c> nuqtasiz <c>"ı"</c> beradi va <c>"individual"</c>
    /// hech qachon topilmasdi (CA1305/CA1311).
    /// </summary>
    private static string Key(string? raw) =>
        (raw ?? string.Empty).Trim().ToLowerInvariant();
}
