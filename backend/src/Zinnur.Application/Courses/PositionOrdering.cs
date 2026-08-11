using System.Globalization;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Courses.Dtos;

namespace Zinnur.Application.Courses;

/// <summary>
/// ========================================================================
/// TARTIB (`Position`) — RAQAMLASH VA "REORDER" QOIDASI: YAGONA JOY
/// ========================================================================
///
/// ★ NIMA UCHUN AJRATILDI: bu mantiq <c>CourseService</c> ichida edi va
/// endi uni dars mediasi (<c>LessonAssetService</c>) hamda vazifa sharti
/// biriktirmalari ham ishlatadi. Nusxalansa, bir kuni ulardan birida
/// "to'liq ro'yxat" tekshiruvi bo'shashardi va YARIM tartib yozilib
/// qolardi — dars videolari o'quvchida boshqa ketma-ketlikda ko'rinardi.
///
/// Uchala amal bir xil shartnomaga bo'ysunadi:
///   • YARATISHDA raqam MAVJUD MAKSIMUMDAN keyingisi (`Count` EMAS);
///   • O'CHIRISHDA qolganlar QAYTA raqamlanadi — "teshik" qolmaydi;
///   • REORDER butun ro'yxatni 0,1,2... qilib ZICH qiladi, BITTA
///     `SaveChanges` = BITTA tranzaksiya ichida.
/// </summary>
internal static class PositionOrdering
{
    internal const string OrderedIdsField = "orderedIds";

    /// <summary>
    /// Ro'yxatni 0,1,2... qilib ZICH qayta raqamlaydi va yangi raqamlarni
    /// qaytaradi.
    ///
    /// Kirish ro'yxati ALLAQACHON kerakli tartibda bo'lishi kerak — bu metod
    /// tartiblamaydi, faqat RAQAMLAYDI.
    /// </summary>
    internal static List<PositionDto> Reindex<T>(
        IReadOnlyList<T> ordered, Func<T, long> id, Action<T, int> setPosition)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(setPosition);

        var result = new List<PositionDto>(ordered.Count);

        for (var index = 0; index < ordered.Count; index++)
        {
            setPosition(ordered[index], index);
            result.Add(new PositionDto(id(ordered[index]), index));
        }

        return result;
    }

    /// <summary>
    /// So'ralgan Id ketma-ketligi bo'yicha qatorlarni saflaydi.
    ///
    /// ★★ TO'LIQLIK QAT'IY TEKSHIRILADI: takror Id, yetishmayotgan element
    /// yoki begona Id bo'lsa — 400 va HECH NARSA yozilmaydi.
    ///
    /// NIMA UCHUN SHUNCHALIK QAT'IY: yarim ro'yxat qabul qilinsa,
    /// yuborilmagan elementlarni qayerga qo'yish kerakligi noaniq bo'lardi
    /// (boshigami, oxirigami?) va ikki foydalanuvchi bir vaqtda
    /// tartiblaganda natija aytib bo'lmaydigan bo'lardi. Darslar uchun
    /// bunga qo'shimcha sabab bor: GATING aynan shu tartibga tayanadi.
    /// </summary>
    internal static List<T> ArrangeByRequest<T>(
        List<T> rows, ReorderRequest request, Func<T, long> id, string what)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(id);

        var requested = request.OrderedIds;

        if (requested is null || requested.Count == 0)
            throw Invalid(what + " tartibi uchun ro'yxat bo'sh bo'lmasligi kerak.");

        var seen = new HashSet<long>(requested.Count);

        foreach (var value in requested)
        {
            if (!seen.Add(value))
            {
                throw Invalid(
                    "Ro'yxatda takrorlangan Id bor: "
                    + value.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (requested.Count != rows.Count)
        {
            var mismatch = string.Create(
                CultureInfo.InvariantCulture,
                $"Ro'yxat to'liq emas: {rows.Count} ta element kutilgan edi, {requested.Count} ta keldi.");

            throw Invalid(mismatch + " Tartiblashda BARCHA elementlar yuborilishi shart.");
        }

        var arranged = new List<T>(rows.Count);

        foreach (var value in requested)
        {
            var row = rows.Find(candidate => id(candidate) == value)
                ?? throw Invalid(
                    what + " ro'yxatiga tegishli bo'lmagan Id: "
                    + value.ToString(CultureInfo.InvariantCulture));

            arranged.Add(row);
        }

        return arranged;
    }

    /// <summary>
    /// Yangi element uchun tartib raqami — MAVJUD maksimumdan keyingisi.
    ///
    /// `Count` EMAS: eski ma'lumotda tartib zich bo'lmasligi mumkin (seed
    /// `Position = 1` dan boshlaydi), o'shanda `Count` mavjud raqamga
    /// tushib qolardi va ikki element bir xil o'ringa da'vo qilardi.
    /// </summary>
    internal static async Task<int> NextPositionAsync(
        IQueryable<int> positions, CancellationToken ct) =>
        (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .MaxAsync(positions, position => (int?)position, ct)
            .ConfigureAwait(false) ?? -1) + 1;

    private static ValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [OrderedIdsField] = [message],
        });
}
