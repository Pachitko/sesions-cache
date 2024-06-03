using System.ComponentModel.DataAnnotations;

namespace Server.Options;

public sealed class ServerOptions
{
    [Range(1025, ushort.MaxValue)]
    public required ushort ListenLocalPort { get; set; }
    
    [Range(1025, ushort.MaxValue)]
    public required ushort SiloPort { get; set; }
    
    [Range(1025, ushort.MaxValue)]
    public required ushort GatewayPort { get; set; }

    public required TimeSpan ReplicationRetryDelay { get; set; }

    public bool EnableConcurrencyCheckForSections { get; set; }
}