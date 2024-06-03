using Grains.States;

namespace Grains.Messages;

using SectionId = String;

[Serializable]
[GenerateSerializer]
public sealed class SessionData
{
    [Id(0)]
    public required long ExpirationUnixSeconds { get; set; }

    [Id(1)]
    public IDictionary<SectionId, SectionData> Data { get; init; } = new Dictionary<SectionId, SectionData>();

    [Id(2)]
    public long Version { get; set; }

    public static SessionData FromSessionState(SessionState sessionState, ICollection<string> sections)
    {
        return new SessionData
        {
            ExpirationUnixSeconds = sessionState.ExpirationUnixSeconds,
            Data = sections.Count == 0 ? sessionState.Data : GetSections(sessionState, sections),
            Version = sessionState.Version
        };
    }

    private static Dictionary<SectionId, SectionData> GetSections(SessionState sessionState, IEnumerable<string> sections)
    {
        var result = new Dictionary<SectionId, SectionData>();

        foreach (var section in sections)
        {
            if (sessionState.Data.TryGetValue(section, out var value))
            {
                result.Add(section, value);
            }
        }
        
        return result;
    }
}