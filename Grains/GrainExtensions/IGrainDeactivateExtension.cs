using Orleans.Runtime;

namespace Grains.GrainExtensions;

public interface IGrainDeactivateExtension : IGrainExtension
{
    Task Deactivate(string message);
}