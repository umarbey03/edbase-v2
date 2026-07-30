using System.Globalization;
using Zinnur.Domain.Exceptions;

namespace Zinnur.Domain.Finance;

/// <summary>
/// Kvitansiya raqami: <c>ZN-2026-07-000123</c> — oy ichida ketma-ket.
///
/// FORMAT SHU YERDA QULFLANADI: raqam ota-onaga qog'ozda beriladi va nizoda
/// qidiriladi. Eski tizimda u satr sifatida servis ichida yasalardi;
/// tartib raqami oldiga nol qo'yilmasa (<c>...-123</c>) ro'yxatda
/// <c>-1000</c> dan keyin turib qolardi va "oxirgi raqam" noto'g'ri
/// topilardi — ya'ni ikki kvitansiya bir xil raqam olishi mumkin edi.
/// </summary>
public readonly record struct ReceiptNumber
{
    private const string Prefix = "ZN";
    private const int SequenceDigits = 6;

    private ReceiptNumber(BillingPeriod period, int sequence)
    {
        Period = period;
        Sequence = sequence;
    }

    public BillingPeriod Period { get; }

    /// <summary>Oy ichidagi tartib raqami (1 dan boshlanadi).</summary>
    public int Sequence { get; }

    public static ReceiptNumber Create(BillingPeriod period, int sequence)
    {
        if (sequence < 1)
            throw new DomainException("Kvitansiya tartib raqami 1 dan boshlanadi.");

        if (sequence > 999_999)
            throw new DomainException("Oylik kvitansiya chegarasi (999 999) tugadi.");

        return new ReceiptNumber(period, sequence);
    }

    /// <summary>Oldingi raqamdan keyingisini yasaydi (yangi oyda 1 dan boshlanadi).</summary>
    public static ReceiptNumber Next(BillingPeriod period, ReceiptNumber? previous) =>
        previous is { } prior && prior.Period == period
            ? Create(period, prior.Sequence + 1)
            : Create(period, 1);

    public static ReceiptNumber Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // ZN-YYYY-MM-NNNNNN
        var parts = value.Split('-');
        if (parts.Length != 4
            || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
        {
            throw new DomainException($"Kvitansiya raqami formati noto'g'ri: '{value}'.");
        }

        var period = BillingPeriod.Parse($"{parts[1]}-{parts[2]}");
        return Create(period, sequence);
    }

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}-{Period}-{Sequence.ToString($"D{SequenceDigits.ToString(CultureInfo.InvariantCulture)}", CultureInfo.InvariantCulture)}");
}
