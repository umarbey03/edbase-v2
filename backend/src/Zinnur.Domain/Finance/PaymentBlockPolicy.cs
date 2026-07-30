using Zinnur.Domain.Enums;

namespace Zinnur.Domain.Finance;

/// <summary>
/// Qarzdorlik uchun bloklash qoidasi.
///
/// Uch shart BIRGALIKDA bajarilsagina blok tushadi:
///   1) qarz chegaradan OSHGAN (teng bo'lsa — bloklanmaydi);
///   2) o'quvchi istisno emas;
///   3) sozlamadagi qamrov so'ralayotgan turkumni O'Z ICHIGA OLADI.
///
/// NIMA UCHUN DOMAIN'DA: eski tizimda bu shartlar endpointlar bo'ylab
/// tarqalgan edi va ba'zi joyda `>=`, ba'zi joyda `>` yozilgandi — bir xil
/// qarzli o'quvchi bir sahifada bloklanib, boshqasida o'tib ketardi.
/// </summary>
public static class PaymentBlockPolicy
{
    /// <summary>
    /// Qamrov ierarxiyasi: <c>Platform</c> hammasini, <c>Live</c> video va
    /// jonlini, <c>Video</c> faqat videoni yopadi.
    /// </summary>
    public static bool Covers(PaymentBlockScope configured, PaymentBlockScope requested) =>
        configured != PaymentBlockScope.None
        && requested != PaymentBlockScope.None
        && configured >= requested;

    /// <summary>
    /// Bloklanadimi. <paramref name="enforce"/> — global "yumshoq rejim"
    /// kaliti: sinov muhitida qarz bo'lsa ham hech kim bloklanmasin.
    /// </summary>
    public static bool IsBlocked(
        decimal debt,
        decimal threshold,
        PaymentBlockScope configured,
        PaymentBlockScope requested,
        bool exempt,
        bool enforce = true)
    {
        if (!enforce || exempt) return false;
        if (debt <= threshold) return false;
        return Covers(configured, requested);
    }
}
