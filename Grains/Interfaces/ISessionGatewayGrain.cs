namespace Grains.Interfaces;

public interface ISessionGatewayGrain : IGrainWithGuidKey
{
    Task Add(string key);
    Task<int?> Get(string key);
}