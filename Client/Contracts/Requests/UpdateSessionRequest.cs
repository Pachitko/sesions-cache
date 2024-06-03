namespace Client.Contracts.Requests;

public sealed class UpdateSessionRequest
{
    public Guid SessionId { get; set; }
    public int? TimeToLive { get; set; }
    public SectionRequest[] Sections { get; set; } = Array.Empty<SectionRequest>();
}