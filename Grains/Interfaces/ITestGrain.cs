namespace Grains.Interfaces;

public interface ITestGrain : IGrainWithStringKey
{
    ValueTask<int> Test();
}