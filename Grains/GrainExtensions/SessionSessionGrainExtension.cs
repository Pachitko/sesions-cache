using Orleans.Runtime;

namespace Grains.GrainExtensions;

public sealed class SessionSessionGrainExtension(IGrainContext context) : ISessionGrainExtension
{
    public ValueTask<SiloAddress?> GetSiloAddress()
    {
        return ValueTask.FromResult(context.Address.SiloAddress);
    }
}