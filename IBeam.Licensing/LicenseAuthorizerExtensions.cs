namespace IBeam.Licensing;

public static class LicenseAuthorizerExtensions
{
    public static async Task RequireEntitlementAsync(
        this ILicenseAuthorizer authorizer,
        Guid tenantId,
        LicenseSubject subject,
        string entitlement,
        CancellationToken ct = default)
    {
        if (authorizer is null)
            throw new ArgumentNullException(nameof(authorizer));

        var result = await authorizer.AuthorizeAsync(tenantId, subject, entitlement, ct).ConfigureAwait(false);
        if (!result.Allowed)
            throw new LicensingException(result.Reason ?? $"License entitlement '{entitlement}' is required.");
    }
}
