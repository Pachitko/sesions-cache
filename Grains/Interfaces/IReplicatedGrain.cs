namespace Grains.Interfaces;

public interface IReplicatedGrain : IGrainWithGuidKey
{
    Task Add();
    Task<int> Get();
}