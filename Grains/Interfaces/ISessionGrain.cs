using Grains.Messages;
using Grains.States;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace Grains.Interfaces;

public interface ISessionGrain : IGrainWithGuidCompoundKey
{
    // [ResponseTimeout("00:00:10")]
    Task<SiloAddress> Update(UpdateSessionCommand command);
    
    [ReadOnly]
    // [ResponseTimeout("00:00:02")]
    Task<SessionData?> Get(GetSessionDataQuery dataQuery);
    
    // [ResponseTimeout("00:00:10")]
    ValueTask<bool> Invalidate(InvalidateCommand command);
}