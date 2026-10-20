using System.Text.Json;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Models;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Management.Services.Diagnostics;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services.CustomerIdentity;

internal sealed record PortalMutationState(ManagementIdentityOperationLookup Lookup, string IdempotencyKey, Guid? TargetOperationId, DateTimeOffset ExpiresUtc)
{ public override string ToString() => "Portal mutation state (key/reference redacted)"; }
/// <summary>Stateless encrypted references. Stores no directory payloads; the key exists only inside authenticated ciphertext.</summary>
public sealed class PortalMutationReferences(IOptions<ManagementPortalCustomerDirectoryOptions> options, TimeProvider clock)
{
    private const string Purpose = "portal-customer-mutation";
    internal PortalMutationState Create(ManagementIdentityOperationLookup lookup, string key) => new(lookup, key, null,
        clock.GetUtcNow().AddMinutes(options.Value.TokenLifetimeMinutes));
    internal ManagementPortalOperationResult Result(ManagementCustomerIdentityConnection c, PortalMutationState state, ManagementIdentityOperationDto? result)
    {
        var next = state with { TargetOperationId = result?.OperationId ?? state.TargetOperationId };
        var reference = Codec().Protect(Purpose, Context(c, state.Lookup.Operation), next);
        var onboarding = result?.Operation is ManagementCustomerIdentityOperations.OnboardingCreate or ManagementCustomerIdentityOperations.OnboardingRetry;
        return new(reference, result?.Onboarding?.Status ?? (onboarding ? "unknown" : result?.Status ?? "unknown"), result, state.ExpiresUtc);
    }
    internal PortalMutationState Read(ManagementCustomerIdentityConnection c, string authority, string reference)
    {
        var state = Codec().Unprotect<PortalMutationState>(Purpose, Context(c, authority), reference);
        if (state.Lookup is null || state.Lookup.Operation != authority || !ManagementDelegationBinding.Mutating(authority) ||
            !ManagementDelegationBinding.ValidIdempotencyKey(state.IdempotencyKey) || state.Lookup.ExistingResourceId == Guid.Empty || state.TargetOperationId == Guid.Empty ||
            state.ExpiresUtc <= clock.GetUtcNow() || state.ExpiresUtc > clock.GetUtcNow().AddMinutes(options.Value.TokenLifetimeMinutes))
            throw new ManagementCustomerIdentityInvalidRequestException();
        return state;
    }
    private DiagnosticTokenCodec Codec() => new(Convert.FromBase64String(options.Value.ProtectionKey));
    private static string Context(ManagementCustomerIdentityConnection c, string authority) => JsonSerializer.Serialize(new {
        c.OrganizationId, c.UserId, c.ApplicationId, c.IdentityRealmId, c.CredentialId, c.ApplicationKey, c.Environment, c.BaseUrl, c.Version, authority });
}
