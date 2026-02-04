namespace IBeam.Identity.Repositories.EntityFramework.Options;

public enum EfProvider
{
    Sqlite = 1,
    SqlServer = 2,
    Postgres = 3
}

public sealed class EntityFrameworkIdentityOptions
{
    // Default is SQLite as requested
    public EfProvider Provider { get; init; } = EfProvider.Sqlite;

    // Required for real providers (Sqlite/SqlServer/Postgres)
    public string ConnectionString { get; init; } = string.Empty;

    // Optional: where migrations live (defaults to this assembly)
    public string? MigrationsAssembly { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException("IdentityEf:ConnectionString is required.");
    }
}
