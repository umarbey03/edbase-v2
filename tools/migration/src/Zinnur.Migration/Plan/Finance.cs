using System.Globalization;
using Zinnur.Migration.Mapping;
using Zinnur.Migration.Pipeline;
using static Zinnur.Migration.Plan.MigrationPlan;

namespace Zinnur.Migration.Plan;

/// <summary>
/// ========================================================================
/// MOLIYA — TARIF, CHEGIRMA, TO'LOV, JURNAL, AUDIT
/// ========================================================================
///
/// ★ BU YERDA XATO ENG QIMMAT: bir necha yuz mingdan bir necha million
/// so'mgacha. Shuning uchun uchta qat'iy qoida:
///
///   1. PUL <c>decimal</c> (<c>numeric(18,2)</c>). <c>double</c> HECH
///      QACHON: <c>540000.0 / 3 * 3</c> ham <c>539999.99999</c> beradi.
///   2. HAR o'zgartirilgan tiyin HISOBOTDA. Qiymat kesilsa yoki qator
///      tushib qolsa, uning summasi "ko'chmagan pul" hisobiga qo'shiladi
///      va oxirida <c>manba = ko'chgan + ko'chmagan</c> tengligi
///      TEKSHIRILADI.
///   3. v2 CHECK cheklovlariga OLDINDAN moslashtiriladi. Aks holda
///      ko'chirish tunning o'rtasida <c>CK_Payments_...</c> xatosi bilan
///      yiqilardi va sababini topish uchun qo'lda SQL yozish kerak bo'lardi.
/// </summary>
internal static class Finance
{
    // ====================================================================
    // TARIFLAR
    // ====================================================================

    /// <summary>
    /// <c>tariffs</c> -> <c>Tariffs</c>.
    ///
    /// ⚠️ YO'QOTISH: eski <c>note</c> (tarif izohi) va <c>created_by</c>
    /// (kim yaratgani) ustunlarining v2 da mosi YO'Q.
    /// </summary>
    public static TableSpec Tariffs() => new()
    {
        Name = "tariffs -> Tariffs",
        SourceTable = "tariffs",
        TargetTable = "Tariffs",
        SourceCountSql = "SELECT COUNT(*) FROM tariffs",
        SourceSql = """
            SELECT id, name, amount, lessons_count, course_id, group_id,
                   active_from, is_active, created_at
            FROM tariffs
            ORDER BY id
            """,
        Columns =
        [
            Id(), Str("Name"), Money("Amount"), Num("LessonsCount"), Ref("CourseId"),
            Ref("GroupId"), Day("ActiveFrom"), Flag("IsActive"),
            Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var amount = ctx.Money(2);
            if (amount < 0)
            {
                // `CK_Tariffs_Amount_NonNegative`. Manfiy narx ma'nosiz —
                // ko'chirilsa hisobotlarda "manfiy tushum" chiqardi.
                ctx.Report.AddSkippedMoney("Tariffs.Amount", amount);
                return ctx.Skip("Tarif summasi MANFIY (v2 cheklovi rad etadi)", RowContext.Str(amount));
            }

            var lessons = ctx.Int32OrNull(3) ?? 8;
            if (lessons is < 1 or > 60)
            {
                ctx.Fixed(
                    "Darslar soni 1..60 dan tashqarida (CK_Tariffs_LessonsCount_Range) — 8 ga tuzatildi",
                    RowContext.Str(lessons));
                lessons = 8;
            }

            var courseId = ctx.Int64OrNull(4);
            if (!ctx.State.HasOptional("courses", courseId))
            {
                ctx.Fixed("Kurs ko'chmagan — tarif umumiyga aylandi", RowContext.Str(courseId!.Value));
                courseId = null;
            }

            var groupId = ctx.Int64OrNull(5);
            if (!ctx.State.HasOptional("groups", groupId))
            {
                ctx.Fixed("Guruh ko'chmagan — tarif umumiyga aylandi", RowContext.Str(groupId!.Value));
                groupId = null;
            }

            ctx.Report.AddMoney("Tariffs.Amount", amount);

            return
            [
                ctx.Id,
                RowContext.Clip(ctx.Text(1)?.Trim(), 200) is { Length: > 0 } n ? n : "Nomsiz tarif",
                amount,
                lessons,
                courseId,
                groupId,
                ctx.Date(6, DateOnly.FromDateTime(Fallback.UtcDateTime)),
                ctx.Bool(7, true),
                ctx.Instant(8),
                null,
            ];
        },
    };

    // ====================================================================
    // CHEGIRMALAR
    // ====================================================================

    /// <summary>
    /// <c>student_discounts</c> -> <c>StudentDiscounts</c>.
    ///
    /// ★ ESKI SXEMADA IKKI USTUN — <c>reason</c> (turkum: <c>sibling</c>,
    /// <c>social</c>, <c>merit</c>, <c>other</c>) va <c>note</c> (erkin
    /// matn). v2 da BITTA <c>Reason</c> (erkin matn) bor. Ikkalasi
    /// BIRLASHTIRILADI (<c>"sibling — aka-ukasi bor"</c>), chunki faqat
    /// bittasini olish xodim yozgan izohni yoki chegirma turkumini
    /// yo'qotardi.
    ///
    /// ★ v2 CHECK: <c>Value &gt; 0</c>, foizli bo'lsa <c>Value &lt;= 100</c>,
    /// va <c>ValidTo &gt;= ValidFrom</c>. Eski bazada bu cheklovlar
    /// YO'Q edi.
    /// </summary>
    public static TableSpec StudentDiscounts() => new()
    {
        Name = "student_discounts -> StudentDiscounts",
        SourceTable = "student_discounts",
        TargetTable = "StudentDiscounts",
        SourceCountSql = "SELECT COUNT(*) FROM student_discounts",
        SourceSql = """
            SELECT id, student_id, group_id, kind, value, reason, note,
                   valid_from, valid_to, is_active, created_at
            FROM student_discounts
            ORDER BY id
            """,
        Columns =
        [
            Id(), Ref("StudentId"), Ref("GroupId"), Num("Kind"), Money("Value"),
            Day("ValidFrom"), Day("ValidTo"), Flag("IsActive"), Str("Reason"),
            Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var studentId = ctx.Int64(1);
            if (!ctx.State.Has("users", studentId))
                return ctx.Skip("O'quvchi ko'chmagan", RowContext.Str(studentId));

            if (!LegacyMap.TryDiscountKind(ctx.Text(3), out var kind))
                return ctx.Skip("Chegirma turi tanilmadi", ctx.Text(3));

            var value = ctx.Money(4);
            if (value <= 0)
            {
                return ctx.Skip(
                    "Chegirma qiymati 0 yoki manfiy (CK_StudentDiscounts_Value_Range)",
                    RowContext.Str(value));
            }

            if (kind == Domain.Enums.DiscountKind.Percent && value > 100)
            {
                return ctx.Skip(
                    "Foizli chegirma 100 dan katta (CK_StudentDiscounts_Value_Range)",
                    RowContext.Str(value));
            }

            var groupId = ctx.Int64OrNull(2);
            if (!ctx.State.HasOptional("groups", groupId))
            {
                ctx.Fixed(
                    "Guruh ko'chmagan — chegirma BARCHA guruhlarga tegishli bo'lib qoldi",
                    RowContext.Str(groupId!.Value));
                groupId = null;
            }

            var validFrom = ctx.Date(7, DateOnly.FromDateTime(Fallback.UtcDateTime));
            var validTo = ctx.DateOrNull(8);
            if (validTo is not null && validTo.Value < validFrom)
            {
                ctx.Fixed(
                    "Chegirma tugash sanasi boshlanishidan oldin (CK_StudentDiscounts_Valid_Range) — bo'shatildi",
                    validTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                validTo = null;
            }

            return
            [
                ctx.Id,
                studentId,
                groupId,
                (int)kind,
                value,
                validFrom,
                validTo,
                ctx.Bool(9, true),
                RowContext.Clip(Merge(ctx.Text(5), ctx.Text(6)), 500),
                ctx.Instant(10),
                null,
            ];
        },
    };

    // ====================================================================
    // OYLIK TO'LOVLAR
    // ====================================================================

    /// <summary>
    /// <c>payments</c> -> <c>Payments</c>.
    ///
    /// ★★ v2 DAGI TO'RTTA CHECK CHEKLOVI — ESKI BAZADA HECH BIRI YO'Q EDI:
    ///   • <c>Amount = BaseAmount - DiscountAmount</c>
    ///   • <c>DiscountAmount &lt;= BaseAmount</c>
    ///   • <c>0 &lt;= PaidAmount &lt;= Amount</c>
    ///   • <c>Period</c> aynan <c>YYYY-MM</c>
    ///
    /// ★ <c>BaseAmount</c> ESKI BAZADA IXTIYORIY (u keyinroq qo'shilgan
    /// ustun). Qaror: <c>BaseAmount = Amount + DiscountAmount</c> —
    /// ya'ni "chegirmadan oldingi narx". Bu barcha uchta tenglikni
    /// AVTOMATIK bajaradi va <c>Amount</c> ga (o'quvchi HAQIQATAN qarzdor
    /// bo'lgan summaga) UMUMAN TEGMAYDI. Teskarisi — <c>Amount</c> ni
    /// <c>BaseAmount - DiscountAmount</c> ga tenglashtirish — qarz
    /// summasini jimgina o'zgartirardi, ya'ni PULNI o'zgartirardi.
    ///
    /// ★ <c>PaidAmount &gt; Amount</c> holati (ortiqcha to'lov) eski
    /// tizimda BO'LGAN. v2 da ortiqcha pul <c>StudentAccounts.Balance</c>
    /// ga boradi, <c>Payments</c> ga emas. Kesilgan qism "ko'chmagan pul"
    /// hisobiga yoziladi va hisobotda ALOHIDA ko'rinadi — bu qo'lda
    /// balansga o'tkazilishi kerak bo'lgan summa.
    /// </summary>
    public static TableSpec Payments() => new()
    {
        Name = "payments -> Payments",
        SourceTable = "payments",
        TargetTable = "Payments",
        SourceCountSql = "SELECT COUNT(*) FROM payments",
        SourceSql = """
            SELECT id, student_id, group_id, period, amount, paid_amount,
                   base_amount, discount_amount, status, paid_at, method,
                   note, marked_by, created_at
            FROM payments
            ORDER BY id
            """,
        Columns =
        [
            Id(), Ref("StudentId"), Ref("GroupId"), Str("Period"), Money("Amount"),
            Money("BaseAmount"), Money("DiscountAmount"), Money("PaidAmount"),
            Num("Status"), Moment("PaidAt"), Num("Method"), Str("Note"),
            Ref("MarkedById"), Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var studentId = ctx.Int64(1);
            var groupId = ctx.Int64(2);
            var amount = ctx.Money(4);
            var paid = ctx.Money(5);

            if (!ctx.State.Has("users", studentId))
            {
                ctx.Report.AddSkippedMoney("Payments.Amount", amount);
                ctx.Report.AddSkippedMoney("Payments.PaidAmount", paid);
                return ctx.Skip("O'quvchi ko'chmagan", RowContext.Str(studentId));
            }

            if (!ctx.State.Has("groups", groupId))
            {
                ctx.Report.AddSkippedMoney("Payments.Amount", amount);
                ctx.Report.AddSkippedMoney("Payments.PaidAmount", paid);
                return ctx.Skip("Guruh ko'chmagan", RowContext.Str(groupId));
            }

            var period = (ctx.Text(3) ?? string.Empty).Trim();
            if (!IsPeriod(period))
            {
                ctx.Report.AddSkippedMoney("Payments.Amount", amount);
                ctx.Report.AddSkippedMoney("Payments.PaidAmount", paid);
                return ctx.Skip("Davr `YYYY-MM` ko'rinishida emas (CK_Payments_Period_Format)", period);
            }

            if (!LegacyMap.TryPaymentStatus(ctx.Text(8), out var status))
            {
                ctx.Report.AddSkippedMoney("Payments.Amount", amount);
                ctx.Report.AddSkippedMoney("Payments.PaidAmount", paid);
                return ctx.Skip("To'lov holati tanilmadi", ctx.Text(8));
            }

            if (amount < 0)
            {
                ctx.Report.AddSkippedMoney("Payments.Amount", amount);
                ctx.Report.AddSkippedMoney("Payments.PaidAmount", paid);
                return ctx.Skip("Oylik summa MANFIY (CK_Payments_Amounts_NonNegative)", RowContext.Str(amount));
            }

            // --- chegirma ---
            var discount = ctx.Money(7);
            if (discount < 0)
            {
                ctx.Fixed("Chegirma summasi manfiy — 0 ga tuzatildi", RowContext.Str(discount));
                discount = 0m;
            }

            // --- tarif narxi (chegirmadan oldingi) ---
            var legacyBase = ctx.MoneyOrNull(6);
            var expected = amount + discount;

            if (legacyBase is null)
            {
                ctx.Fixed("Tarif narxi (base_amount) eski qatorda yo'q — Amount + Discount deb hisoblandi");
            }
            else if (legacyBase.Value != expected)
            {
                ctx.Fixed(
                    "base_amount - discount_amount != amount edi — BaseAmount qayta hisoblandi (Amount o'zgarmadi)",
                    RowContext.Str(legacyBase.Value) + " -> " + RowContext.Str(expected));
            }

            // --- tushgan pul ---
            if (paid < 0)
            {
                ctx.Fixed("To'langan summa manfiy — 0 ga tuzatildi", RowContext.Str(paid));
                ctx.Report.AddSkippedMoney("Payments.PaidAmount", paid);
                paid = 0m;
            }
            else if (paid > amount)
            {
                var excess = paid - amount;
                ctx.Fixed(
                    "ORTIQCHA TO'LOV: PaidAmount > Amount (CK_Payments_Paid_WithinAmount) — kesildi, "
                    + "ortiqcha qism QO'LDA balansga o'tkazilishi kerak",
                    RowContext.Str(excess));
                ctx.Report.AddSkippedMoney("Payments.PaidAmount", excess);
                paid = amount;
            }

            var markedBy = ctx.Int64OrNull(12);
            if (!ctx.State.HasOptional("users", markedBy)) markedBy = null;

            var method = LegacyMap.Method(ctx.Text(10), out var methodKnown);
            var note = ctx.Text(11);
            if (!methodKnown)
            {
                ctx.Fixed("To'lov usuli tanilmadi — usul bo'sh qoldi, asl matn izohga qo'shildi", ctx.Text(10));
                note = Merge(note, "eski usul: " + (ctx.Text(10) ?? string.Empty));
            }

            ctx.Report.AddMoney("Payments.Amount", amount);
            ctx.Report.AddMoney("Payments.PaidAmount", paid);
            ctx.State.Add("payments", ctx.Id);

            return
            [
                ctx.Id,
                studentId,
                groupId,
                period,
                amount,
                expected,                       // BaseAmount = Amount + Discount
                discount,
                paid,
                (int)status,
                ctx.InstantOrNull(9),
                method is null ? null : (int)method.Value,
                RowContext.Clip(note, 500),
                markedBy,
                ctx.Instant(13),
                null,
            ];
        },
    };

    // ====================================================================
    // MOLIYA JURNALI
    // ====================================================================

    /// <summary>
    /// <c>payment_transactions</c> -> <c>PaymentTransactions</c>.
    ///
    /// ★★ <c>type = 'due'</c> QATORLARI KO'CHIRILMAYDI. Sabab
    /// <see cref="LegacyMap.TryTransactionKind"/> da: ular pul HARAKATI
    /// emas, "shu oyga qarz yozuvi ochildi" degan belgi. v2 da bu holat
    /// <c>Payments</c> qatorining O'ZIDA turadi. Ko'chirilsa kunlik kassa
    /// hisoboti markazga TUSHMAGAN pulni ko'rsatardi — bu eng qimmat
    /// turdagi hisobot xatosi.
    ///
    /// ★ <c>Amount &gt; 0</c> (<c>CK_PaymentTransactions_Amount_Positive</c>):
    /// nol summali "harakat" hech narsa emas, manfiysi esa v2 da
    /// <c>Kind = Refund</c> bilan MUSBAT summa sifatida yoziladi.
    ///
    /// ★ KVITANSIYA RAQAMI v2 da FILTRLANGAN UNIKAL. Eski tizimda cheklov
    /// yo'q edi, ya'ni takror raqamlar bo'lishi mumkin: eng kichik
    /// <c>id</c> li yozuv raqamni oladi, qolganlarida <c>ReceiptNo</c>
    /// <c>NULL</c> bo'ladi va asl raqam IZOHGA yoziladi (ma'lumot
    /// yo'qolmaydi).
    ///
    /// ⚠️ YO'QOTISH: <c>payment_id</c> (qaysi oyga tegishli),
    /// <c>lesson_id</c> va <c>lessons_count</c> ustunlarining v2 da
    /// mosi yo'q.
    /// </summary>
    public static TableSpec PaymentTransactions() => new()
    {
        Name = "payment_transactions -> PaymentTransactions",
        SourceTable = "payment_transactions",
        TargetTable = "PaymentTransactions",
        SourceCountSql = "SELECT COUNT(*) FROM payment_transactions",
        SourceSql = """
            SELECT id, student_id, group_id, amount, type, method, note,
                   receipt_no, created_by, created_at
            FROM payment_transactions
            ORDER BY id
            """,
        Columns =
        [
            Id(), Ref("StudentId"), Ref("GroupId"), Num("Kind"), Money("Amount"),
            Str("ReceiptNo"), Num("Method"), Str("Note"), Ref("ActorId"),
            Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var studentId = ctx.Int64(1);
            var amount = ctx.Money(3);

            if (!ctx.State.Has("users", studentId))
            {
                ctx.Report.AddSkippedMoney("PaymentTransactions.Amount", amount);
                return ctx.Skip("O'quvchi ko'chmagan", RowContext.Str(studentId));
            }

            if (!LegacyMap.TryTransactionKind(ctx.Text(4), out var kind))
            {
                ctx.Report.AddSkippedMoney("PaymentTransactions.Amount", amount);

                return string.Equals(ctx.Text(4)?.Trim(), "due", StringComparison.OrdinalIgnoreCase)
                    ? ctx.Skip("`due` yozuvi ATAYLAB ko'chirilmaydi (pul harakati emas — oy ochilgani belgisi)")
                    : ctx.Skip("Jurnal yozuvi turi tanilmadi", ctx.Text(4));
            }

            var note = ctx.Text(6);

            if (amount < 0)
            {
                // Eski tizim qaytarilgan pulni ba'zan MANFIY summa bilan
                // yozgan. v2 da ishora TURDA (`Refund`), summada emas.
                ctx.Fixed(
                    "Manfiy summa — `Refund` turiga o'tkazilib, musbat qilindi",
                    RowContext.Str(amount));

                var positive = -amount;

                // ★ BELGI ALMASHINUVI OSHKOR HISOBGA OLINADI. Manba
                // yig'indisida summa MANFIY turibdi, v2 ga esa MUSBAT
                // yoziladi — ya'ni yig'indi 2 x summa ga farq qiladi. Bu farq
                // qayd etilmasa `manba = ko'chgan + ko'chmagan` tengligi
                // buzilardi va tekshiruv oddiy belgi o'girishni HAQIQIY pul
                // yo'qotishidan ajrata olmasdi (ikkalasi ham bir xil
                // "hisobga olinmagan" xatosini berardi).
                ctx.Report.AddSkippedMoney("PaymentTransactions.Amount", amount - positive);

                kind = Domain.Enums.PaymentTransactionKind.Refund;
                amount = positive;
            }

            if (amount == 0)
                return ctx.Skip("Summasi 0 (CK_PaymentTransactions_Amount_Positive)");

            var groupId = ctx.Int64OrNull(2);
            if (!ctx.State.HasOptional("groups", groupId))
            {
                ctx.Fixed("Guruh ko'chmagan — jurnal yozuvi guruhsiz qoldi", RowContext.Str(groupId!.Value));
                groupId = null;
            }

            string? receipt = RowContext.Clip(ctx.Text(7)?.Trim(), 32);
            if (receipt is { Length: 0 }) receipt = null;

            if (receipt is not null && ctx.State.ReceiptDuplicateLosers.Contains(ctx.Id))
            {
                ctx.Fixed(
                    "Kvitansiya raqami TAKRORLANGAN (v2 da unikal) — raqam bo'shatildi, asli izohga yozildi",
                    receipt);
                note = Merge(note, "eski kvitansiya: " + receipt);
                receipt = null;
            }

            var method = LegacyMap.Method(ctx.Text(5), out var methodKnown);
            if (!methodKnown)
            {
                ctx.Fixed("To'lov usuli tanilmadi — usul bo'sh qoldi, asl matn izohga qo'shildi", ctx.Text(5));
                note = Merge(note, "eski usul: " + (ctx.Text(5) ?? string.Empty));
            }

            var actorId = ctx.Int64OrNull(8);
            if (!ctx.State.HasOptional("users", actorId)) actorId = null;

            ctx.Report.AddMoney("PaymentTransactions.Amount", amount);

            return
            [
                ctx.Id,
                studentId,
                groupId,
                (int)kind,
                amount,
                receipt,
                method is null ? null : (int)method.Value,
                RowContext.Clip(note, 500),
                actorId,
                ctx.Instant(9),
                null,
            ];
        },
    };

    // ====================================================================
    // AUDIT IZI
    // ====================================================================

    /// <summary>
    /// <c>payment_audit</c> -> <c>PaymentAudits</c>.
    ///
    /// ★ AUDIT IZI ATAYLAB KO'CHIRILADI: "kim, qachon, nimani
    /// o'zgartirdi" tarixi ko'chirishda tashlab ketilsa, ko'chirishdan
    /// OLDINGI davr bo'yicha hech qanday savolga javob bera olmasdik
    /// (masalan "bu o'quvchining qarzi qachon va kim tomonidan
    /// kechirilgan?").
    ///
    /// ⚠️ <c>EntityId</c> ESKI ID: u endi v2 dagi qatorga ishora qiladi,
    /// chunki id'lar SAQLANADI. Agar id'lar qayta berilganda bu ustun
    /// ma'nosini yo'qotardi — bu id saqlash qarorining yana bir sababi.
    /// </summary>
    public static TableSpec PaymentAudits() => new()
    {
        Name = "payment_audit -> PaymentAudits",
        SourceTable = "payment_audit",
        TargetTable = "PaymentAudits",
        SourceCountSql = "SELECT COUNT(*) FROM payment_audit",
        SourceSql = """
            SELECT id, entity, entity_id, student_id, action, field,
                   old_value, new_value, note, actor_id, created_at
            FROM payment_audit
            ORDER BY id
            """,
        Columns =
        [
            Id(), Str("Entity"), Big("EntityId"), Ref("StudentId"), Str("Action"),
            Str("Field"), Str("OldValue"), Str("NewValue"), Str("Note"), Ref("ActorId"),
            Moment("CreatedAt"), Moment("UpdatedAt"),
        ],
        Map = ctx =>
        {
            var entity = RowContext.Clip(ctx.Text(1)?.Trim(), 32);
            if (string.IsNullOrEmpty(entity))
                return ctx.Skip("Audit yozuvida obyekt turi bo'sh (v2 da majburiy)");

            var action = RowContext.Clip(ctx.Text(4)?.Trim(), 32);
            if (string.IsNullOrEmpty(action))
                return ctx.Skip("Audit yozuvida amal bo'sh (v2 da majburiy)");

            var studentId = ctx.Int64OrNull(3);
            if (!ctx.State.HasOptional("users", studentId)) studentId = null;

            var actorId = ctx.Int64OrNull(9);
            if (!ctx.State.HasOptional("users", actorId)) actorId = null;

            return
            [
                ctx.Id,
                entity,
                ctx.Int64OrNull(2),
                studentId,
                action,
                RowContext.Clip(ctx.Text(5), 64),
                RowContext.Clip(ctx.Text(6), 500),
                RowContext.Clip(ctx.Text(7), 500),
                RowContext.Clip(ctx.Text(8), 500),
                actorId,
                ctx.Instant(10),
                null,
            ];
        },
    };

    // ====================================================================
    // YORDAMCHILAR
    // ====================================================================

    /// <summary>
    /// Ikki matnni bitta ustunga sig'diradi (<c>"a — b"</c>).
    /// Bo'shlari tashlab yuboriladi, ikkalasi ham bo'sh bo'lsa <c>null</c>.
    /// </summary>
    private static string? Merge(string? first, string? second)
    {
        var a = first?.Trim();
        var b = second?.Trim();

        if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? null : b;
        return string.IsNullOrEmpty(b) ? a : a + " — " + b;
    }

    /// <summary>
    /// <c>YYYY-MM</c> tekshiruvi (<c>CK_Payments_Period_Format</c> bilan AYNI).
    ///
    /// Regex ATAYLAB ISHLATILMAGAN: qoida juda oddiy, regex esa har
    /// qatorda ishlaganda sezilarli sekinlashtirardi va CA1307/CA1310
    /// tuzoqlarini qo'shardi.
    /// </summary>
    private static bool IsPeriod(string value)
    {
        if (value.Length != 7 || value[4] != '-') return false;

        for (var i = 0; i < 4; i++)
        {
            if (!char.IsAsciiDigit(value[i])) return false;
        }

        if (!char.IsAsciiDigit(value[5]) || !char.IsAsciiDigit(value[6])) return false;

        var month = ((value[5] - '0') * 10) + (value[6] - '0');
        return month is >= 1 and <= 12;
    }
}
