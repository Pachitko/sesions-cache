namespace Client.Contracts.Requests;

public sealed record SectionRequest(string Key, byte[] Value, int Version);