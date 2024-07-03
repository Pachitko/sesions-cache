using System.ComponentModel.DataAnnotations;

namespace Client.Options;

public sealed class ClientOptions
{
    [Required]
    [Range(1, 10 * 1024 * 1024)]
    public required int MaxSectionSize { get; set; }
    
    [Required]
    public required string ConnectionString { get; set; }

    [Required]
    [Range(0, 2)]
    public required ushort ReplicationFactor { get; set; }
}