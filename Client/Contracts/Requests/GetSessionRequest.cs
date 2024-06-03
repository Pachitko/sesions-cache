using Microsoft.AspNetCore.Mvc;

namespace Client.Contracts.Requests;

public sealed class GetSessionRequest
{
    public Guid SessionId { get; set; }

    public string[] Sections { get; set; } = Array.Empty<string>();
}