using Grains.Messages;
using Grains.States;
using Orleans.Concurrency;

namespace Grains.Interfaces;

public interface ISessionGrain : IGrainWithGuidCompoundKey
{
    // [ResponseTimeout("00:00:10")]
    Task Update(UpdateSessionCommand command);
    
    [ReadOnly]
    // [ResponseTimeout("00:00:02")]
    Task<SessionState> Get();
    
    // [ResponseTimeout("00:00:10")]
    ValueTask Invalidate();
}