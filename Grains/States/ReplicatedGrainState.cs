namespace Grains.States;

public sealed class AddEvent;

public sealed class ReplicatedGrainState
{
    public int Value { get; set; }

    public void Apply(AddEvent addEvent)
    {
        Value++;
    }
}