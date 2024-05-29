namespace Grains.Exception;

[GenerateSerializer]
public sealed class ConcurrencyException : System.Exception
{
    public ICollection<string> SectionIds { get; set; } = Array.Empty<string>();
}