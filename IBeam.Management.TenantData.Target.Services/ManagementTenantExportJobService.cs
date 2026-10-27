using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.TenantData;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.TenantData.Target.Services;

public sealed class ManagementTenantExportJobService(IManagementTenantExportJobRepository repository, IManagementOperationAccessService access,
    IManagementTenantExportHandler handler, IManagementTenantExportPolicy policy, IOptions<ManagementTenantExportOptions> module,
    IOptions<ManagementTenantExportJobOptions> options, IOptions<ManagementDiagnosticsOptions> deployment, IOptions<ManagementOperationTargetOptions> target,
    TimeProvider clock, IServiceOperationExecutor operations) : IManagementTenantExportJobService
{
    public int ProtocolVersion => 1;
    public bool Durable => repository.Durable;
    public Task<ManagementTenantExportResult> SubmitAsync(ManagementTenantExportExecution execution, ManagementTenantExportRequest request, CancellationToken ct = default)
        => Run(execution, async token =>
        {
            Validate(execution);
            if (request is null || execution.Authority.ResourceVersion != request.ExpectedVersion || !ManagementTenantExportProtocol.Key(request.PolicyKey) ||
                !module.Value.PolicyKeys.Contains(request.PolicyKey, StringComparer.Ordinal) || request.ExpectedVersion is not { Length: > 0 and <= 256 } ||
                request.ExpectedVersion.Trim() != request.ExpectedVersion || request.ExpectedVersion == "*" || request.ExpectedVersion.Any(char.IsControl)) throw new ManagementOperationInvalidException();
            var intent = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { request.PolicyKey, request.ExpectedVersion })));
            var existing = await repository.GetAsync(execution, token);
            if (existing is not null)
            {
                if (existing.IntentHash != intent) throw new ManagementOperationConflictException();
                await policy.RequireStatusAsync(existing.Execution, token);
                return Project(existing, "accepted", clock.GetUtcNow());
            }
            var definition = ManagementTenantExportProtocol.Definition(handler.Describe());
            var native = execution with { PolicyKey = request.PolicyKey, InventoryVersion = definition.InventoryVersion, LeaseFence = null };
            await policy.RequireAsync(native, request.ExpectedVersion, token);
            await access.RequireAsync(ManagementTenantDataOperations.Export, execution.Authority, token);
            var now = clock.GetUtcNow();
            var row = new ManagementTenantExportJobEntity { Execution = native, Definition = definition, IntentHash = intent, ExpectedTenantVersion = request.ExpectedVersion,
                CreatedUtc = now, UpdatedUtc = now, ExpiresUtc = now.AddMinutes(options.Value.RetentionMinutes),
                Coverage = definition.Datasets.Select(x => new ManagementTenantExportCoverage(x.Key, x.Kind, "pending")).ToArray() };
            if (!await repository.TryCreateAsync(row, token))
            {
                existing = await repository.GetAsync(execution, token) ?? throw new ManagementOperationUnavailableException();
                if (existing.IntentHash != intent) throw new ManagementOperationConflictException();
                await policy.RequireStatusAsync(existing.Execution, token);
                return Project(existing, "accepted", clock.GetUtcNow());
            }
            return Project(row, "accepted", now);
        }, ct);
    public Task<ManagementTenantExportResult> FindAsync(ManagementTenantExportExecution execution, CancellationToken ct = default)
        => Run(execution, async token =>
        {
            Validate(execution);
            var row = await repository.GetAsync(execution, token);
            if (row is null) return new(execution.Authority.TenantId, execution.JobId, "unknown", null);
            await policy.RequireStatusAsync(row.Execution, token);
            return Project(row, "found", clock.GetUtcNow());
        }, ct, lookup: true);
    internal static ManagementTenantExportResult Project(ManagementTenantExportJobEntity row, string outcome, DateTimeOffset now)
    {
        var expired = now >= row.ExpiresUtc && !row.CancelRequested;
        var state = row.CancelRequested ? ManagementTenantExportStates.Cancelled : expired ? ManagementTenantExportStates.Expired : row.State;
        var status = new ManagementTenantExportJobStatus(1, row.Execution.Authority.TenantId, row.Execution.JobId, state, row.CreatedUtc, row.UpdatedUtc,
            row.ExpiresUtc, row.Definition.Consistency, row.Coverage, expired ? "expired" : row.CancelRequested ? "cancelled" : row.FailureCode);
        return ManagementTenantExportProtocol.Result(new(status.TenantId, status.JobId, outcome, status), status.TenantId, status.JobId, row.Definition);
    }
    internal static void Validate(ManagementTenantExportExecution execution)
    {
        var s = execution.Authority;
        if (s.OrganizationId == Guid.Empty || s.ApplicationId == Guid.Empty || s.RealmId == Guid.Empty || s.TenantId == Guid.Empty || s.ActorUserId == Guid.Empty ||
            s.OperationId == Guid.Empty || s.OperationId != execution.JobId || s.ResourceId != s.TenantId || string.IsNullOrWhiteSpace(execution.ApplicationKey) ||
            execution.ApplicationKey.Length > 100 || execution.ApplicationKey.Any(char.IsControl) || string.IsNullOrWhiteSpace(execution.Environment) || execution.Environment.Length > 100 || execution.Environment.Any(char.IsControl))
            throw new ManagementAccessDeniedException();
    }
    private Task<ManagementTenantExportResult> Run(ManagementTenantExportExecution execution, Func<CancellationToken, Task<ManagementTenantExportResult>> action, CancellationToken ct, bool lookup = false)
        => operations.ExecuteAsync(this, async token =>
        {
            try
            {
                ManagementTenantExportDeploymentGuard.Require(execution, deployment.Value, target.Value);
                await access.RequireAsync(ManagementTenantDataOperations.Export, execution.Authority, token);
                var result = await action(token);
                await access.RequireAsync(ManagementTenantDataOperations.Export, execution.Authority, token);
                return result;
            }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationInvalidException or ManagementOperationConflictException or ManagementOperationPreconditionException or ManagementOperationUnavailableException) &&
                !(ex is OperationCanceledException && token.IsCancellationRequested)) { throw new ManagementOperationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = lookup ? "management.operations.lookup" : ManagementTenantDataOperations.Export,
            AuditAction = lookup ? "management.operations.lookup" : ManagementTenantDataOperations.Export, EntityId = execution.Authority.TenantId,
            EntityName = "ManagementTenantExportJob", PermissionEnabled = false, AuditEnabled = true }, ct);
}
