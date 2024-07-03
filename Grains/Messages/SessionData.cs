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
    public List<SectionData> Data { get; set; } = new();

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

    private static List<SectionData> GetSections(SessionState sessionState, IEnumerable<string> sections)
    {
        var result = new List<SectionData>();
        result
            .AddRange(sections
                .Select(section => sessionState.Data.FirstOrDefault(x => x.Name == section))
                .Where(sectionData => sectionData is not null)!
                );

        return result;
    }
}