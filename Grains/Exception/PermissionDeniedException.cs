namespace Grains.Exception;

public sealed class PermissionDeniedException(string? message = null) : System.Exception(message);