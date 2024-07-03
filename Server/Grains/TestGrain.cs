using Grains.Interfaces;
using Grains.States;

namespace Server.Grains;

public sealed class TestGrain : Grain, ITestGrain
{
    public ValueTask<int> Test()
    {
        return ValueTask.FromResult(1);
    }
}