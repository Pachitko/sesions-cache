namespace Grains.Messages;

[GenerateSerializer]
[Immutable]
public sealed class GetSessionDataQuery
{
    [Id(0)] public string[] Sections { get; set; } = Array.Empty<string>();
}