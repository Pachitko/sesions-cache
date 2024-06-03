namespace Infrastructure.Options;

public sealed class DatabaseOptions
{
    public required string ConnectionString { get; set; } = null!;
}