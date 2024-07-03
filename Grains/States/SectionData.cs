namespace Grains.States;

[GenerateSerializer]
public sealed class SectionData
{
    [Id(0)] public required string Name { get; set; }
    
    [Id(1)] public long Version { get; set; } = 1;
    
    [Id(2)] public byte[] Data { get; set; } = Array.Empty<byte>();
}