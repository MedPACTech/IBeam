using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Operations;

/// <summary>Durable orchestration only. Each business adapter must reconcile and use stable-ID/ETag downstream writes.</summary>
public sealed class ManagementIdentityOperationService(IManagementIdentityOperationRepository repository,
    IManagementIdentityOperationAuditRepository audit, IManagementCustomerIdentityTargetAccessService access,
    IOptions<ManagementIdentityOperationOptions> options, TimeProvider clock, IServiceOperationExecutor operations) : IManagementIdentityOperationService, IManagementIdentityOperationLookupService
{
    public bool SupportsLookup => true;
    public Task<ManagementIdentityOperationDto> ExecuteAsync(ManagementIdentityMutationRequest request, IManagementIdentityMutationHandler handler, CancellationToken ct = default)
        => Run("execute", async token =>
        {
            if (request is null || handler is null || !ManagementDelegationBinding.Mutating(request.Operation) ||
                request.ExistingResourceId == Guid.Empty || !ManagementDelegationBinding.ValidIdempotencyKey(request.IdempotencyKey)) throw new ManagementCustomerIdentityInvalidRequestException();
            var actor = await access.RequireAsync(request.Operation, token);
            var scope = Scope(actor);
            var key = Convert.FromBase64String(options.Value.FingerprintKey);
            var binding = JsonSerializer.Serialize(new { scope, actor.ApplicationKey, actor.Environment, request.Operation, request.ResourceScope, request.ExistingResourceId });
            var id = new Guid(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(binding + "\n" + request.IdempotencyKey)).AsSpan(0, 16));
            var canonical = Canonicalize(request.CanonicalPayload);
            var fingerprint = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(binding + "\n" + canonical)));
            var row = await repository.GetAsync(scope, id, token);
            if (row is null)
            {
                var now = clock.GetUtcNow();
                var reserved = new ManagementIdentityOperationEntity { IdentityRealmId = scope.IdentityRealmId, OrganizationId = scope.OrganizationId,
                    ApplicationId = scope.ApplicationId, ApplicationKey = actor.ApplicationKey, Environment = actor.Environment, OperationId = id,
                    Operation = request.Operation, ResourceScope = request.ResourceScope, ResourceId = request.ExistingResourceId ??
                        new Guid(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("resource\n" + binding + "\n" + request.IdempotencyKey)).AsSpan(0, 16)),
                    Fingerprint = fingerprint, ActorUserId = actor.UserId, CredentialId = actor.CredentialId, CorrelationId = actor.CorrelationId,
                    CreatedUtc = now, UpdatedUtc = now, ExpiresUtc = now.AddDays(options.Value.RetentionDays), IntentAuditId = Guid.NewGuid() };
                await repository.TryCreateAsync(reserved, token); // conflict means another worker won; always re-read its stable ID.
                row = await repository.GetAsync(scope, id, token) ?? throw new ManagementCustomerIdentityUnavailableException();
            }
            Validate(row, actor, request.Operation);
            if (row.Fingerprint != fingerprint || row.ResourceScope != request.ResourceScope || row.ExpiresUtc <= clock.GetUtcNow())
                throw new ManagementIdentityOperationConflictException();
            row = await Deliver(row, token);
            await access.RequireAsync(request.Operation, token);
            if (row.Status == "succeeded") return Project(row);
            if (row.LeaseUntilUtc > clock.GetUtcNow()) return Project(row);
            if (row.Attempts >= options.Value.MaximumAttempts || row.ExpiresUtc <= clock.GetUtcNow().AddSeconds(options.Value.LeaseSeconds))
                throw new ManagementIdentityOperationConflictException();
            await access.RequireAsync(request.Operation, token);
            var claim = Guid.NewGuid(); var recovering = row.Attempts > 0;
            try {
                row = await repository.ReplaceAsync(row with { Status = "running", LeaseId = claim,
                    LeaseUntilUtc = clock.GetUtcNow().AddSeconds(options.Value.LeaseSeconds), UpdatedUtc = clock.GetUtcNow(), Attempts = row.Attempts + 1,
                    AttemptActorUserId = actor.UserId, AttemptCredentialId = actor.CredentialId, AttemptCorrelationId = actor.CorrelationId }, row.Version, token);
            } catch (ManagementIdentityOperationConflictException) {
                var current = await Require(id, actor, request.Operation, token);
                await access.RequireAsync(request.Operation, token); return Project(current);
            }
            var context = new ManagementIdentityMutationContext(id, row.ResourceId, row.ResourceScope, row.Step, recovering);
            var completed = false;
            try
            {
                // Always reconcile first, including a first attempt: provider writes must remain conditional even if a prior lease owner resumes.
                var state = await handler.ReconcileAsync(context, token);
                if (state == ManagementIdentityReconciliation.Unknown) {
                    row = await Finish(row, "uncertain", token); completed = true;
                    await access.RequireAsync(request.Operation, token); return Project(row);
                }
                if (state != ManagementIdentityReconciliation.Applied && state != ManagementIdentityReconciliation.Absent)
                    throw new ManagementCustomerIdentityUnavailableException();
                if (state == ManagementIdentityReconciliation.Absent) {
                    await access.RequireAsync(request.Operation, token);
                    await handler.ApplyAsync(context, token);
                }
                await access.RequireAsync(request.Operation, token);
                row = await Finish(row, "succeeded", token);
                completed = true;
                row = await Deliver(row, token);
                await access.RequireAsync(request.Operation, token);
                return Project(row);
            }
            catch
            {
                // A lost reply or cancellation cannot prove whether the downstream write happened. Preserve that uncertainty.
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                if (!completed) try { await Finish(row, "uncertain", recovery.Token); } catch { /* Running claim still forces reconciliation after lease expiry. */ }
                throw;
            }
        }, ct);

    public Task<ManagementIdentityOperationDto> GetAsync(Guid operationId, string authority, CancellationToken ct = default)
        => Run("read", async token => {
            var actor = await access.RequireAsync(authority, token);
            var row = await Require(operationId, actor, authority, token);
            await access.RequireAsync(authority, token);
            return Project(row);
        }, ct);
    public Task<ManagementIdentityOperationDto> ResolveAsync(ManagementIdentityOperationLookup request, string idempotencyKey, string authority, CancellationToken ct = default)
        => Run("resolve", async token => {
            var actor = await access.RequireAsync(authority, token);
            if (request is null || !ManagementDelegationBinding.ValidIdempotencyKey(idempotencyKey) || !ManagementDelegationBinding.Mutating(request.Operation) ||
                request.ExistingResourceId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
            if (request.Operation != authority && !(authority == ManagementCustomerIdentityOperations.OnboardingRead &&
                request.Operation is ManagementCustomerIdentityOperations.OnboardingCreate or ManagementCustomerIdentityOperations.OnboardingRetry))
                throw new ManagementAccessDeniedException();
            var binding = JsonSerializer.Serialize(new { scope = Scope(actor), actor.ApplicationKey, actor.Environment,
                request.Operation, request.ResourceScope, request.ExistingResourceId });
            var key = Convert.FromBase64String(options.Value.FingerprintKey);
            var id = new Guid(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(binding + "\n" + idempotencyKey)).AsSpan(0, 16));
            var row = await Require(id, actor, authority, token);
            await access.RequireAsync(authority, token);
            return Project(row);
        }, ct);
    public Task DeliverAuditAsync(Guid operationId, string authority, CancellationToken ct = default)
        => Run("audit", async token => {
            var actor = await access.RequireAsync(authority, token);
            var row = await Require(operationId, actor, authority, token);
            await Deliver(row, token); return true;
        }, ct);

    private async Task<ManagementIdentityOperationEntity> Require(Guid id, ManagementDelegatedActor actor, string authority, CancellationToken ct)
    {
        if (id == Guid.Empty || !ManagementDelegationBinding.Mutating(authority) && authority != ManagementCustomerIdentityOperations.OnboardingRead)
            throw new ManagementCustomerIdentityInvalidRequestException();
        var row = await repository.GetAsync(Scope(actor), id, ct) ?? throw new ManagementCustomerIdentityNotFoundException();
        Validate(row, actor, authority);
        if (row.ExpiresUtc <= clock.GetUtcNow()) throw new ManagementCustomerIdentityNotFoundException();
        return row;
    }
    private static void Validate(ManagementIdentityOperationEntity row, ManagementDelegatedActor actor, string authority)
    {
        if (Scope(actor) != new ManagementIdentityOperationScope(row.IdentityRealmId, row.OrganizationId, row.ApplicationId) ||
            row.ApplicationKey != actor.ApplicationKey || row.Environment != actor.Environment || !ManagementDelegationBinding.Mutating(row.Operation) ||
            row.OperationId == Guid.Empty || row.ResourceId == Guid.Empty || row.ActorUserId == Guid.Empty || row.CredentialId == Guid.Empty ||
            row.Fingerprint.Length != 64 || row.Status is not ("pending" or "running" or "succeeded" or "uncertain") || row.Step is < 0 or > 1 ||
            row.IntentAuditId == Guid.Empty || row.CorrelationId.Length != 32 || row.ExpiresUtc <= row.CreatedUtc)
            throw new ManagementCustomerIdentityUnavailableException();
        if (row.Operation != authority && !(authority == ManagementCustomerIdentityOperations.OnboardingRead &&
            row.Operation is ManagementCustomerIdentityOperations.OnboardingCreate or ManagementCustomerIdentityOperations.OnboardingRetry))
            throw new ManagementAccessDeniedException();
    }
    private async Task<ManagementIdentityOperationEntity> Finish(ManagementIdentityOperationEntity row, string status, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return await repository.ReplaceAsync(row with { Status = status, Step = status == "succeeded" ? 1 : row.Step, UpdatedUtc = now,
            LeaseId = null, LeaseUntilUtc = null, ResultAuditId = Guid.NewGuid(), ResultUtc = now, ResultStatus = status, ResultPending = true }, row.Version, ct);
    }
    private async Task<ManagementIdentityOperationEntity> Deliver(ManagementIdentityOperationEntity row, CancellationToken ct)
    {
        if (row.IntentPending) {
            await audit.AppendOnceAsync(Event(row, row.IntentAuditId, row.CreatedUtc, "intent", false), ct);
            row = await Acknowledge(row, true, ct);
        }
        if (row.ResultPending) {
            if (row.ResultAuditId is not Guid id || row.ResultUtc is not DateTimeOffset time || row.ResultStatus is not ("succeeded" or "uncertain"))
                throw new ManagementCustomerIdentityUnavailableException();
            await audit.AppendOnceAsync(Event(row, id, time, row.ResultStatus, row.ResultStatus == "succeeded"), ct);
            row = await Acknowledge(row, false, ct);
        }
        return row;
    }
    private async Task<ManagementIdentityOperationEntity> Acknowledge(ManagementIdentityOperationEntity row, bool intent, CancellationToken ct)
    {
        try { return await repository.ReplaceAsync(intent ? row with { IntentPending = false } : row with { ResultPending = false }, row.Version, ct); }
        catch (ManagementIdentityOperationConflictException) {
            var current = await repository.GetAsync(new(row.IdentityRealmId, row.OrganizationId, row.ApplicationId), row.OperationId, ct)
                ?? throw new ManagementCustomerIdentityUnavailableException();
            if (current.Fingerprint != row.Fingerprint || current.Operation != row.Operation || current.ResourceId != row.ResourceId ||
                (intent ? current.IntentPending : current.ResultPending))
                throw new ManagementCustomerIdentityUnavailableException();
            return current;
        }
    }
    private static ManagementAuditEntity Event(ManagementIdentityOperationEntity row, Guid id, DateTimeOffset time, string phase, bool success)
        => new() { AuditId = id, OccurredUtc = time, ExpiresUtc = row.ExpiresUtc, OrganizationId = row.OrganizationId,
            ApplicationId = row.ApplicationId, ActorUserId = phase == "intent" ? row.ActorUserId : row.AttemptActorUserId,
            CredentialId = phase == "intent" ? row.CredentialId : row.AttemptCredentialId, SubjectId = row.ResourceId,
            SubjectType = ManagementSubjectTypes.Credential, ApplicationKey = row.ApplicationKey, Environment = row.Environment,
            Operation = row.Operation + "." + phase, Succeeded = success, CorrelationId = phase == "intent" ? row.CorrelationId : row.AttemptCorrelationId ?? row.CorrelationId };
    private static ManagementIdentityOperationScope Scope(ManagementDelegatedActor actor) => new(actor.IdentityRealmId, actor.OrganizationId, actor.ApplicationId);
    private static ManagementIdentityOperationDto Project(ManagementIdentityOperationEntity row)
        => new(row.OperationId, row.Operation, row.Status, row.Step, row.ResourceId, row.CreatedUtc, row.UpdatedUtc, row.ExpiresUtc);
    private static string Canonicalize(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length is < 2 or > 32768) throw new ManagementCustomerIdentityInvalidRequestException();
        try {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ManagementCustomerIdentityInvalidRequestException();
            using var buffer = new MemoryStream(); using (var writer = new Utf8JsonWriter(buffer)) Write(document.RootElement, writer);
            return Encoding.UTF8.GetString(buffer.ToArray());
        } catch (JsonException) { throw new ManagementCustomerIdentityInvalidRequestException(); }
    }
    private static void Write(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object) {
            var fields = value.EnumerateObject().ToArray();
            if (fields.Length > 100 || fields.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length)
                throw new ManagementCustomerIdentityInvalidRequestException();
            writer.WriteStartObject(); foreach (var field in fields.OrderBy(x => x.Name, StringComparer.Ordinal)) { writer.WritePropertyName(field.Name); Write(field.Value, writer); } writer.WriteEndObject();
        } else if (value.ValueKind == JsonValueKind.Array) {
            if (value.GetArrayLength() > 100) throw new ManagementCustomerIdentityInvalidRequestException();
            writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(item, writer); writer.WriteEndArray();
        } else value.WriteTo(writer);
    }
    private Task<T> Run<T>(string action, Func<CancellationToken, Task<T>> run, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            try { if (!options.Value.IsValid()) throw new ManagementCustomerIdentityUnavailableException(); return await run(timeout.Token); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or
                ManagementIdentityOperationConflictException or ManagementCustomerIdentityPreconditionException or ManagementCustomerIdentityNotFoundException or ManagementCustomerIdentityUnavailableException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.identity.operations." + action, AuditAction = "management.identity.operations." + action,
            EntityName = "ManagementIdentityOperation", PermissionEnabled = false, AuditEnabled = true }, ct);
}
