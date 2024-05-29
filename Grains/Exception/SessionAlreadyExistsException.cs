namespace Grains.Exception;

[GenerateSerializer]
public sealed class SessionAlreadyExistsException(string? message = null) : System.Exception(message);