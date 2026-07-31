using NpgsqlTypes;

namespace Zinnur.Migration.Pipeline;

/// <summary>Maqsad ustuni: nomi va ANIQ Postgres turi.</summary>
/// <param name="Name">v2 dagi ustun nomi (PascalCase, tirnoq ichida yoziladi).</param>
/// <param name="Type">
/// Parametr turi. OSHKOR ko'rsatilishi SHART: <c>NULL</c> qiymat uchun
/// Npgsql turni o'zi topa olmaydi va "could not determine data type"
/// bilan yiqiladi. Bundan tashqari ro'yxatning o'zi kodda sxema
/// hujjatiga aylanadi.
/// </param>
internal readonly record struct TargetColumn(string Name, NpgsqlDbType Type);

/// <summary>
/// Bitta jadvalni ko'chirish TA'RIFI (deklarativ).
///
/// ★ NIMA UCHUN DEKLARATIV: 25 ta jadval uchun 25 ta qo'lda yozilgan
/// <c>INSERT</c> sikli bo'lsa, paketlash / idempotentlik / progress /
/// xatolarni sanash mantig'i 25 marta takrorlanardi va bittasida
/// unutilardi (aynan shunday xatolar ko'chirishda eng ko'p uchraydi).
/// Bu yerda takrorlanadigan qism BITTA — <see cref="TableCopier"/>,
/// jadvalga xos qism esa faqat SQL va xaritalash funksiyasi.
/// </summary>
internal sealed class TableSpec
{
    /// <summary>Hisobotdagi nom, masalan <c>"users -> Users"</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Eski jadval nomi (sanoq va xato yozuvlari uchun).</summary>
    public required string SourceTable { get; init; }

    /// <summary>v2 jadval nomi.</summary>
    public required string TargetTable { get; init; }

    /// <summary>
    /// Manbadan o'qish so'rovi. <c>ORDER BY</c> MAJBURIY (aniq va takrorlanadigan
    /// tartib — qayta yurgizishda bir xil natija bo'lishi uchun).
    /// </summary>
    public required string SourceSql { get; init; }

    /// <summary>
    /// Manbadagi qatorlar sonini beruvchi so'rov. <see cref="SourceSql"/> dagi
    /// filtr bilan AYNAN bir xil bo'lishi kerak — aks holda solishtirish
    /// yolg'on "yo'qolgan qatorlar" ko'rsatardi.
    /// </summary>
    public required string SourceCountSql { get; init; }

    public required IReadOnlyList<TargetColumn> Columns { get; init; }

    /// <summary>
    /// <c>ON CONFLICT (...)</c> ustunlari — idempotentlikning kaliti.
    ///
    /// Standart holda birlamchi kalit (<c>"Id"</c>), chunki eski id'lar
    /// SAQLANADI. Ikki istisno bor:
    ///   • <c>group_members</c> — eski jadvalda <c>id</c> UMUMAN yo'q
    ///     (kalit <c>(group_id, student_id)</c>), shuning uchun konflikt
    ///     TABIIY kalit bo'yicha aniqlanadi;
    ///   • <c>StudentAccounts</c> — v2 da paydo bo'lgan yangi jadval,
    ///     eski <c>users.balance</c> ustunidan hosil bo'ladi va uning
    ///     tabiiy kaliti <c>StudentId</c>.
    /// </summary>
    public string ConflictTarget { get; init; } = "\"Id\"";

    /// <summary>
    /// Bitta qatorni v2 ustunlariga o'giradi.
    /// <c>null</c> qaytarsa — qator O'TKAZIB YUBORILADI (sabab
    /// <see cref="RowContext"/> orqali hisobotga yozilgan bo'lishi shart).
    /// </summary>
    public required Func<RowContext, object?[]?> Map { get; init; }
}
