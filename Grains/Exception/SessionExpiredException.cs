namespace Grains.Exception;

[GenerateSerializer]
public sealed class SessionExpiredException(string? message = null) : System.Exception(message);