namespace Grains.Messages;

[GenerateSerializer]
[Immutable]
public sealed record UpdateSessionCommand
{
    [Immutable]
    [Id(0)]
    public CreateSectionData[] Sections { get; init; } = Array.Empty<CreateSectionData>();
    
    [Immutable]
    [Id(1)]
    public long? ExpirationUnixSeconds { get; init; }

    [Immutable]
    [Id(2)]
    public string? ServiceId { get; set; }
}

[GenerateSerializer]
[Immutable]
public sealed record CreateSectionData
{
    [Id(0)] 
    public required string Key { get; set; }
    
    [Id(1)] 
    public required byte[] Value { get; set; }
    
    [Id(2)]
    public required long Version { get; init; }
} 