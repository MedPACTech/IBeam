using IBeam.Identity.Interfaces;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Tenants;

public sealed class ManagementTenantProvenanceRecorder(IManagementTenantAdministrationRepository repository, IIdentityTenantStore tenants,
    IOptions<ManagementCustomerIdentityTargetOptions> realm, IOptions<ManagementDiagnosticsOptions> deployment,
    IServiceOperationExecutor operations) : IManagementTenantProvenanceRecorder
{
    public Task RecordSelfServiceAsync(Guid tenantId, Guid creationEventId, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token => {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
            if (tenantId == Guid.Empty || creationEventId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
            if (!realm.Value.DedicatedApplicationRealm || realm.Value.IdentityRealmId == Guid.Empty || string.IsNullOrWhiteSpace(deployment.Value.ApplicationKey) || deployment.Value.ApplicationKey.Length > 100 ||
                string.IsNullOrWhiteSpace(deployment.Value.Environment) || deployment.Value.Environment.Length > 100)
                throw new ManagementCustomerIdentityUnavailableException();
            try {
                var native = await tenants.FindByIdAsync(tenantId, deadline.Token);
                if (native?.TenantId != tenantId) throw new ManagementCustomerIdentityNotFoundException();
                var scope = new ManagementTenantAdministrationScope(realm.Value.IdentityRealmId, deployment.Value.ApplicationKey, deployment.Value.Environment);
                var old = await repository.GetAsync(scope, tenantId, deadline.Token);
                if (old is null) {
                    if (await repository.TryCreateAsync(new ManagementTenantAdministrationEntity {
                        IdentityRealmId = scope.IdentityRealmId, ApplicationKey = scope.ApplicationKey, Environment = scope.Environment,
                        TenantId = tenantId, Source = ManagementTenantSources.SelfService, CreateOperationId = creationEventId,
                        LastOperationId = creationEventId, Phase = "ready"
                    }, deadline.Token)) return;
                    old = await repository.GetAsync(scope, tenantId, deadline.Token) ?? throw new ManagementIdentityOperationConflictException();
                }
                if (old.TenantId != tenantId || old.IdentityRealmId != scope.IdentityRealmId || old.ApplicationKey != scope.ApplicationKey || old.Environment != scope.Environment || old.LastOperationId == Guid.Empty || string.IsNullOrWhiteSpace(old.Version))
                    throw new ManagementCustomerIdentityUnavailableException();
                if (old.Source == ManagementTenantSources.SelfService && old.CreateOperationId == creationEventId) return;
                if (old.Source != ManagementTenantSources.Unknown || old.Phase != "ready" || old.CreateOperationId is not null)
                    throw new ManagementIdentityOperationConflictException();
                // Preserve any management mutation's last-operation fence and external reference.
                await repository.ReplaceAsync(old with { Source = ManagementTenantSources.SelfService, CreateOperationId = creationEventId }, old.Version, deadline.Token);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is ManagementCustomerIdentityNotFoundException or ManagementIdentityOperationConflictException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.identity.tenants.provenance", AuditAction = "management.identity.tenants.provenance",
            EntityName = "ManagementTenantProvenance", EntityId = tenantId, PermissionEnabled = false, AuditEnabled = true }, ct);
}
public static class ManagementTenantProvenanceExtensions
{
    /// <summary>Registers only a trusted service; no public API or native signup behavior is changed.</summary>
    public static IServiceCollection AddIBeamManagementTenantProvenance(this IServiceCollection services)
    {
        services.TryAddScoped<IManagementTenantProvenanceRecorder, ManagementTenantProvenanceRecorder>();
        return services;
    }
}
