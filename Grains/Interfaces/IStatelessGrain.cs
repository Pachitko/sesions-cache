namespace Grains.Interfaces;

public interface IStatelessGrain : IGrainWithGuidKey
{
    Task Add(string key);
    Task<int?> Get(string key);
}