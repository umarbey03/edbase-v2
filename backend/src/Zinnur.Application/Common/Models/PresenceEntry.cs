namespace Zinnur.Application.Common.Models;

/// <summary>Jonli darsdagi bitta ishtirokchi (Redis'da saqlanadi).</summary>
public sealed record PresenceEntry(
    long UserId,
    string DisplayName,
    string Role,
    bool HandRaised,
    DateTimeOffset JoinedAt);
