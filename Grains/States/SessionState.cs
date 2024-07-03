using Grains.Messages;

namespace Grains.States;

using SectionId = String;

[Serializable]
[GenerateSerializer]
[Alias("SessionData")]
public sealed class SessionState
{
    [Id(0)]
    [Immutable]
    public required Guid Id { get; init; }

    [Id(1)]
    public required long ExpirationUnixSeconds { get; set; }

    [Id(2)]
    public List<SectionData> Data { get; set; } = new();

    [Id(3)]
    public long Version { get; set; } = 1;
    
    public bool IsEmpty() => Id == Guid.Empty;

    public static SessionState Empty()
    {
        return new SessionState
        {
            Id = Guid.Empty,
            ExpirationUnixSeconds = 0,
            Data = null!,
            Version = 1
        };
    }

    public static SessionState FromSessionData(Guid sessionId, SessionData sessionData)
    {
        return new SessionState
        {
            Id = sessionId,
            ExpirationUnixSeconds = sessionData.ExpirationUnixSeconds,
            Data = sessionData.Data,
            Version = sessionData.Version
        };
    }
}