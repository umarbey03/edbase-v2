namespace Zinnur.Application.LiveSessions.Dtos;

public sealed record LiveSessionDto(
    long Id,
    long GroupId,
    string GroupName,
    string? Title,
    string Type,
    string Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    DateTimeOffset? ActualStart,
    DateTimeOffset? EndsAt,
    bool IsHost);

/// <summary>Frontend LiveKit'ga aynan shu bilan ulanadi.</summary>
public sealed record LiveKitJoinDto(
    string ServerUrl,
    string Token,
    string RoomName,
    bool IsHost,
    DateTimeOffset? EndsAt);

public sealed record ChatMessageDto(
    long Id,
    long SenderId,
    string SenderName,
    string Body,
    DateTimeOffset SentAt);
