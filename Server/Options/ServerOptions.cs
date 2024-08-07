using System.ComponentModel.DataAnnotations;

namespace Server.Options;

public sealed record ServerOptions
{
    [Range(1025, ushort.MaxValue)]
    public required ushort ListenLocalPort { get; set; }
    
    [Range(1025, ushort.MaxValue)]
    public required ushort SiloPort { get; set; }
    
    [Range(1025, ushort.MaxValue)]
    public required ushort GatewayPort { get; set; }
    
    [Required]
    [Range(0, 2)]
    public required ushort ReplicationFactor { get; set; }

    public required TimeSpan ReplicationRetryDelay { get; set; }

    public bool EnableConcurrencyCheckForSections { get; set; }
    
    public bool EnsureSynchronized { get; set; }

    public ReplicationType ReplicationType { get; set; }

    public string? NotificationUrl { get; set; }

    [Required]
    public required TimeSpan OpenPolicyUpdateDelay { get; set; }

    [Required]
    public required string OpenPolicyAgentHost { get; set; }
}