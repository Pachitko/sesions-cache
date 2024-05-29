namespace Grains.States;

using SectionId = String;

[Serializable]
[GenerateSerializer]
[Alias("SessionData")]
public sealed class SessionState
{
    private long _expirationUnixSeconds;

    [Id(0)]
    [Immutable]
    public required Guid Id { get; init; }

    [Id(1)]
    public required long ExpirationUnixSeconds
    {
        get => _expirationUnixSeconds;
        set
        {
            if (_expirationUnixSeconds == value)
                return;
            
            _expirationUnixSeconds = value;
            Version++;
        }
    }

    [Id(2)]
    public IDictionary<SectionId, SectionData> Data { get; init; } = new Dictionary<SectionId, SectionData>();

    [Id(3)]
    public long Version { get; private set; } = 1;
    
    public bool IsEmpty() => Id == Guid.Empty;

    [GenerateSerializer]
    public sealed class SectionData
    {
        [NonSerialized]
        private byte[] _data = Array.Empty<byte>();

        [Id(0)]
        public byte[] Data
        {
            get => _data;
            set
            {
                _data = value;
                Version++;
            }
        }

        [Id(1)]
        public long Version { get; private set; }
    }
}