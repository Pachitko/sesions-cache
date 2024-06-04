namespace Grains.Messages;

[Immutable]
[GenerateSerializer]
public sealed class InvalidateCommand
{
    [Id(0)] public required string Reason { get; set; }
    [Id(1)] public string? ServiceId { get; set; }
}
