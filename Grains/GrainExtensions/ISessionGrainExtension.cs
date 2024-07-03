using Orleans.Runtime;

namespace Grains.GrainExtensions;

public interface ISessionGrainExtension : IGrainExtension
{
    ValueTask<SiloAddress?> GetSiloAddress();
}