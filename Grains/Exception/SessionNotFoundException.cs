namespace Grains.Exception;

[GenerateSerializer]
public sealed class SessionNotFoundException(string? message = null) : System.Exception(message);