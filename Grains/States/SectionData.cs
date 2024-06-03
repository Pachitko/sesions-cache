namespace Grains.States;

[GenerateSerializer]
public sealed class SectionData
{
    [Id(0)] 
    public byte[] Data { get; set; } = Array.Empty<byte>();

    [Id(1)]
    public long Version { get; set; } = 1;
}