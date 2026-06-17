namespace IBeam.Identity.Models;

public sealed record UserExtensionContext(
    string Operation,
    Guid UserId,
    Guid? TenantId = null,
    string? NormalizedEmail = null,
    string? NormalizedPhone = null,
    string? DisplayName = null,
    string? FirstName = null,
    string? LastName = null,
    string? CorrelationId = null,
    string? CausationId = null,
    string? TraceId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static UserExtensionContext Create(
        string operation,
        Guid userId,
        Guid? tenantId = null,
        string? normalizedEmail = null,
        string? normalizedPhone = null,
        string? displayName = null,
        string? firstName = null,
        string? lastName = null,
        string? correlationId = null,
        string? causationId = null,
        string? traceId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
        => new(
            string.IsNullOrWhiteSpace(operation) ? UserExtensionOperations.Ensure : operation.Trim(),
            userId,
            tenantId,
            NormalizeEmail(normalizedEmail),
            NormalizePhone(normalizedPhone),
            NormalizeNullable(displayName),
            NormalizeNullable(firstName),
            NormalizeNullable(lastName),
            correlationId,
            causationId,
            traceId,
            metadata);

    private static string? NormalizeEmail(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string? NormalizePhone(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class UserExtensionOperations
{
    public const string Ensure = "ensure";
    public const string Created = "created";
    public const string Login = "login";
    public const string Selected = "selected";
}
