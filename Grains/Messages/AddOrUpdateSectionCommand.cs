namespace Grains.Messages;

[GenerateSerializer]
[Immutable]
public sealed record UpdateSessionCommand
{
    [Id(0)] public SectionData[] Sections { get; init; } = Array.Empty<SectionData>();
    [Id(1)] public long? ExpirationUnixSeconds { get; init; }
    
    public bool IsEmpty() => Sections.Length == 0 && !ExpirationUnixSeconds.HasValue;
}

[GenerateSerializer]
public sealed record SectionData
{
    [Id(0)] public required string Key { get; set; }
    [Id(1)] public required byte[] Value { get; set; }
    [Id(2)] public required long Version { get; init; }
} 