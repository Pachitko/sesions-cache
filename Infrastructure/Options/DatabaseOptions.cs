namespace Server.Options;

public sealed class DatabaseOptions
{
    public required string SessionDbConnectionString { get; set; } = null!;
}