using Orleans.Runtime;

namespace Grains.GrainExtensions;

public sealed class GrainDeactivateExtension(IGrainContext context) : IGrainDeactivateExtension
{
    public async Task Deactivate(string msg)
    {
        var reason = new DeactivationReason(DeactivationReasonCode.ApplicationRequested, msg);
        await context.DeactivateAsync(reason);
    }
}