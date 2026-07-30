using Zinnur.Domain.Enums;

namespace Zinnur.Application.Scheduling.Dtos;

/// <summary>
/// Jadvaldagi bitta dars.
/// </summary>
/// <param name="RoomName">
/// LiveKit xona nomi. Jadval qayta tuzilganda SAQLANIB QOLGAN darslarda
/// o'zgarmaydi — tashqi havolalar va yozuvlar shu nomga bog'langan.
/// </param>
public sealed record ScheduledSessionDto(
    long Id,
    long GroupId,
    string? Title,
    SessionType Type,
    SessionStatus Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    DateTimeOffset? ActualStart,
    DateTimeOffset? ActualEnd,
    long? HostId,
    string? HostName,
    string RoomName);

/// <summary>
/// Jadvalga NIMA QILINGANI haqida hisobot.
///
/// NIMA UCHUN JAVOBDA QAYTADI (eski tizim darsi): eski panelda guruh
/// tahrirlanganda jadval SHARTSIZ qayta tuzilardi va foydalanuvchi buni
/// BILMASDI — kursni almashtirsangiz ham butun kelajak jadval o'chib qayta
/// yaratilardi, dars Id'lari va LiveKit xona nomlari o'zgarib, tarqatilgan
/// havolalar jimgina buzilardi. Endi har tahrirdan keyin AYNAN nima
/// bo'lgani ko'rinadi.
/// </summary>
/// <param name="ScheduleTouched">Jadvalga umuman tegildimi.</param>
/// <param name="Regenerated">Kelajak darslar qayta tuzildimi (Id'lar o'zgardi).</param>
/// <param name="Created">Yangi yaratilgan darslar soni.</param>
/// <param name="Deleted">O'chirilgan (kelajakdagi, hali boshlanmagan) darslar soni.</param>
/// <param name="Preserved">
/// Tegilmagan darslar soni: o'tgan darslar va holati <c>Live</c>/<c>Ended</c>/
/// <c>Cancelled</c> bo'lganlar — ular davomat va chat tarixini olib yuradi.
/// </param>
/// <param name="HostsUpdated">Dars hosti O'RNIDA yangilangan darslar soni.</param>
/// <param name="TitlesUpdated">Sarlavhasi O'RNIDA yangilangan darslar soni.</param>
/// <param name="Reason">Qarorning inson o'qiy oladigan izohi.</param>
public sealed record ScheduleChangeSummary(
    bool ScheduleTouched,
    bool Regenerated,
    int Created,
    int Deleted,
    int Preserved,
    int HostsUpdated,
    int TitlesUpdated,
    string Reason)
{
    /// <summary>Jadvalga umuman tegilmadi.</summary>
    public static ScheduleChangeSummary Untouched(string reason) =>
        new(ScheduleTouched: false, Regenerated: false,
            Created: 0, Deleted: 0, Preserved: 0,
            HostsUpdated: 0, TitlesUpdated: 0, Reason: reason);

    /// <summary>Jadval qayta tuzilmadi — faqat mavjud darslar o'rnida tahrirlandi.</summary>
    public static ScheduleChangeSummary InPlace(int hostsUpdated, int titlesUpdated, string reason) =>
        new(ScheduleTouched: hostsUpdated + titlesUpdated > 0, Regenerated: false,
            Created: 0, Deleted: 0, Preserved: 0,
            HostsUpdated: hostsUpdated, TitlesUpdated: titlesUpdated, Reason: reason);
}
