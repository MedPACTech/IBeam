using System.Net;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services.CustomerIdentity;

/// <summary>Fixed read paths, exact scoped actor proof, pinned HTTPS and typed allowlisted response projection. No raw proxying.</summary>
public sealed partial class ManagementCustomerIdentityClient(ManagementConnectionNetworkPolicy network, IOptions<ManagementPortalCustomerDirectoryOptions> options,
    IManagementDelegatedActorProvider actors) : IManagementCustomerIdentityClient
{
    private readonly IManagementAuditContext? telemetryAudit;
    public ManagementCustomerIdentityClient(ManagementConnectionNetworkPolicy network, IOptions<ManagementPortalCustomerDirectoryOptions> options,
        IManagementDelegatedActorProvider actors, IManagementAuditContext audit) : this(network, options, actors) => telemetryAudit = audit;
    internal Func<HttpMessageHandler>? TestTransportFactory { get; init; }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<ManagementCustomerDirectoryPage<T>> ListAsync<T>(ManagementCustomerIdentityConnection c, Guid? tenantId, ManagementCustomerDirectoryQuery query, CancellationToken ct = default) where T : class
    {
        var resource = ManagementCustomerDirectoryResource.For<T>(tenantId);
        var parameters = new List<KeyValuePair<string, string>> { new("pageSize", query.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
        if (query.Status is not null) parameters.Add(new("status", query.Status));
        if (query.NamePrefix is not null) parameters.Add(new("namePrefix", query.NamePrefix));
        if (query.ContinuationToken is not null) parameters.Add(new("continuationToken", query.ContinuationToken));
        var path = "/api/management-identity/" + resource.Path + "?" + string.Join("&", parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        await Verify(c, resource.Operation, ct);
        using var body = await Get(c, resource.Operation, path, true, ct);
        var root = body.RootElement;
        var items = Data<T[]>(root) ?? throw new ManagementCustomerIdentityResponseException();
        if (!root.TryGetProperty("pageSize", out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetInt32(out var size) || size != query.PageSize || size is < 1 or > 100 || items.Length > size)
            throw new ManagementCustomerIdentityResponseException();
        string? cursor = null;
        if (root.TryGetProperty("continuationToken", out var value) && value.ValueKind != JsonValueKind.Null)
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 7000 } state)
                throw new ManagementCustomerIdentityResponseException();
            cursor = state;
        }
        return new(items.Select(x => Safe(x, tenantId, null)).ToArray(), size, cursor);
    }
    public async Task<T> FindAsync<T>(ManagementCustomerIdentityConnection c, Guid? tenantId, Guid id, CancellationToken ct = default) where T : class
    {
        if (id == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        var resource = ManagementCustomerDirectoryResource.For<T>(tenantId);
        await Verify(c, resource.Operation, ct);
        using var body = await Get(c, resource.Operation, "/api/management-identity/" + resource.Path + "/" + id.ToString("D"), true, ct);
        return Safe(Data<T>(body.RootElement) ?? throw new ManagementCustomerIdentityResponseException(), tenantId, id);
    }
    private async Task Verify(ManagementCustomerIdentityConnection c, string operation, CancellationToken ct, bool requireReceipts = false)
    {
        using var body = await Get(c, operation, "/api/management-identity/info", false, ct);
        var info = Data<ManagementCustomerIdentityInfo>(body.RootElement) ?? throw new ManagementCustomerIdentityResponseException();
        try { ManagementCustomerIdentityProtocol.Validate(info); } catch { throw new ManagementCustomerIdentityResponseException(); }
        if (info.ApplicationKey != c.ApplicationKey || info.Environment != c.Environment || info.IdentityRealmId != c.IdentityRealmId ||
            !info.Capabilities.Contains(operation, StringComparer.Ordinal) || requireReceipts && info.OperationReceiptProtocolVersion != 1 ||
            operation is ManagementCustomerIdentityOperations.OwnersRead or ManagementCustomerIdentityOperations.OwnersTransfer && info.OwnershipTransferProtocolVersion != 1)
            throw new ManagementCustomerIdentityResponseException();
    }
    public async Task<ManagementIdentityOperationDto> GetOperationAsync(ManagementCustomerIdentityConnection c, Guid operationId, string authority, CancellationToken ct = default)
    {
        if (operationId == Guid.Empty || !IBeam.Management.Services.CustomerIdentity.Delegation.ManagementDelegationBinding.Mutating(authority) &&
            authority != ManagementCustomerIdentityOperations.OnboardingRead) throw new ManagementCustomerIdentityInvalidRequestException();
        await Verify(c, authority, ct);
        using var body = await Get(c, authority, "/api/management-identity/operations/" + operationId.ToString("D"), true, ct);
        var result = Data<ManagementIdentityOperationDto>(body.RootElement) ?? throw new ManagementCustomerIdentityResponseException();
        return SafeOperation(result, authority, operationId);
    }
    public Task<ManagementTenantMutationResult> CreateTenantAsync(ManagementCustomerIdentityConnection c, CreateManagementCustomerTenant request,
        string idempotencyKey, CancellationToken ct = default)
        => MutateTenant(c, null, request, null, idempotencyKey, ct);
    public Task<ManagementTenantMutationResult> RenameTenantAsync(ManagementCustomerIdentityConnection c, Guid tenantId, RenameManagementCustomerTenant request,
        string expectedVersion, string idempotencyKey, CancellationToken ct = default)
        => MutateTenant(c, tenantId, request, expectedVersion, idempotencyKey, ct);
    private async Task<ManagementTenantMutationResult> MutateTenant<T>(ManagementCustomerIdentityConnection c, Guid? tenantId, T request, string? version, string key, CancellationToken ct)
    {
        var operation = tenantId is null ? ManagementCustomerIdentityOperations.TenantsCreate : ManagementCustomerIdentityOperations.TenantsUpdate;
        if (tenantId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        await Verify(c, operation, ct, true);
        var body = JsonSerializer.SerializeToUtf8Bytes(request, Json);
        var response = await Send(c, operation, tenantId is null ? "POST" : "PATCH", "/api/management-identity/tenants" +
            (tenantId is Guid id ? "/" + id.ToString("D") : ""), true, body, key, version, false, ct);
        using var document = response.Document;
        var result = Data<ManagementTenantMutationResult>(document.RootElement) ?? throw new ManagementCustomerIdentityResponseException();
        var receipt = SafeOperation(result.Operation, operation, null);
        var completed = receipt.Status == "succeeded";
        if (tenantId is Guid expected && receipt.ResourceId != expected || !ManagementTenantSources.IsValid(result.Source) ||
            result.ExternalReference is { } reference && (reference.Length is < 1 or > 128 || reference.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':' or '/'))) ||
            completed && response.Status != (tenantId is null ? HttpStatusCode.Created : HttpStatusCode.OK) ||
            !completed && (response.Status != HttpStatusCode.Accepted || result.Tenant is not null) ||
            completed && (result.Tenant is null || tenantId is null && result.Source != "support-provisioned")) throw new ManagementCustomerIdentityResponseException();
        var tenant = result.Tenant is null ? null : Safe(result.Tenant, null, receipt.ResourceId);
        return new(receipt, tenant, result.Source, result.ExternalReference);
    }
    public async Task<ManagementIdentityOperationDto?> ResolveOperationAsync(ManagementCustomerIdentityConnection c, ManagementIdentityOperationLookup request,
        string idempotencyKey, string authority, CancellationToken ct = default)
    {
        await Verify(c, authority, ct, true);
        try {
            var response = await Send(c, authority, "POST", "/api/management-identity/operations/resolve", true,
                JsonSerializer.SerializeToUtf8Bytes(request, Json), idempotencyKey, null, true, ct);
            using var document = response.Document;
            var result = Data<ManagementIdentityOperationDto>(document.RootElement) ?? throw new ManagementCustomerIdentityResponseException();
            var receipt = SafeOperation(result, authority, null);
            if (request.ExistingResourceId is Guid id && receipt.ResourceId != id) throw new ManagementCustomerIdentityResponseException();
            return receipt;
        } catch (ManagementCustomerIdentityNotFoundException) { return null; }
    }
    private static ManagementIdentityOperationDto SafeOperation(ManagementIdentityOperationDto? row, string authority, Guid? id)
    {
        if (row is null || row.OperationId == Guid.Empty || id.HasValue && row.OperationId != id || row.ResourceId == Guid.Empty ||
            row.Status is not ("pending" or "running" or "succeeded" or "uncertain") || row.Step is < 0 or > 1 ||
            (row.Status == "succeeded") != (row.Step == 1) || row.ExpiresUtc <= row.CreatedUtc || row.UpdatedUtc < row.CreatedUtc ||
            row.Operation != authority && !(authority is ManagementCustomerIdentityOperations.OnboardingRead or ManagementCustomerIdentityOperations.OnboardingRetry &&
                row.Operation is ManagementCustomerIdentityOperations.OnboardingCreate or ManagementCustomerIdentityOperations.OnboardingRetry))
            throw new ManagementCustomerIdentityResponseException();
        return new(row.OperationId, row.Operation, row.Status, row.Step, row.ResourceId, row.CreatedUtc, row.UpdatedUtc, row.ExpiresUtc) {
            Onboarding = row.Onboarding is null ? null : SafeOnboarding(row.Onboarding, row),
            Invitation = row.Invitation is null ? null : SafeInvitationOperation(row.Invitation, row) };
    }
    private static T? Data<T>(JsonElement root)
    {
        try { return root.GetProperty("data").Deserialize<T>(Json); }
        catch { throw new ManagementCustomerIdentityResponseException(); }
    }
    private async Task<JsonDocument> Get(ManagementCustomerIdentityConnection c, string operation, string path, bool signed, CancellationToken ct)
        => (await Send(c, operation, "GET", path, signed, default, null, null, false, ct)).Document;
    private sealed record TransportResponse(JsonDocument Document, HttpStatusCode Status);
    private Task<TransportResponse> Send(ManagementCustomerIdentityConnection c, string operation, string method, string path, bool signed,
        ReadOnlyMemory<byte> payload, string? idempotencyKey, string? ifMatch, bool receiptLookup, CancellationToken ct)
        => ManagementTargetTelemetry.Run<TransportResponse>("identity", !signed ? "discovery" : receiptLookup ? "receipt" : method == "GET" ? "read" : "command",
            ManagementTargetTelemetry.Builtin(operation), c.ApplicationId, ManagementTargetTelemetry.Tenant(path), null, telemetryAudit, ct, async () => {
        try
        {
            if (payload.Length > 32768) throw new ManagementCustomerIdentityInvalidRequestException();
            var origin = network.RequireOrigin(c.BaseUrl);
            using var client = ManagementTargetHttpTransport.CreateClient(network, origin, TestTransportFactory);
            var uri = new Uri(origin, path);
            using var request = new HttpRequestMessage(new HttpMethod(method), uri) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            request.Headers.Add("X-IBeam-Management-Key", c.Key.Value);
            if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
            if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            if (!payload.IsEmpty) {
                request.Content = new ByteArrayContent(payload.ToArray());
                request.Content.Headers.ContentType = new("application/json");
            }
            if (signed)
            {
                var assertion = await actors.IssueAsync(c.ApplicationId, operation, new() { Method = method, PathAndQuery = uri.PathAndQuery,
                    Body = payload, IdempotencyKey = idempotencyKey, IfMatch = ifMatch }, ct);
                request.Headers.Add("X-IBeam-Management-Actor", assertion.Value);
            }
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new ManagementAccessDeniedException();
            if (response.StatusCode == HttpStatusCode.NotFound) throw new ManagementCustomerIdentityNotFoundException();
            if (response.StatusCode == HttpStatusCode.BadRequest) throw new ManagementCustomerIdentityInvalidRequestException();
            if (response.StatusCode == HttpStatusCode.Conflict) throw new ManagementIdentityOperationConflictException();
            if (response.StatusCode == HttpStatusCode.PreconditionFailed) throw new ManagementCustomerIdentityPreconditionException();
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new ManagementCustomerIdentityRateLimitException();
            if ((int)response.StatusCode >= 500) throw new ManagementCustomerIdentityUnavailableException();
            var expected = method == "GET" || receiptLookup ? response.StatusCode == HttpStatusCode.OK :
                response.StatusCode == HttpStatusCode.Accepted || response.StatusCode == HttpStatusCode.OK || method == "POST" && response.StatusCode == HttpStatusCode.Created;
            if (!expected || response.Content.Headers.ContentLength > options.Value.MaximumResponseBytes)
                throw new ManagementCustomerIdentityResponseException();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream(); var chunk = new byte[4096];
            while (true)
            {
                var count = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, options.Value.MaximumResponseBytes + 1 - buffer.Length)), ct);
                if (count == 0) break;
                buffer.Write(chunk, 0, count);
                if (buffer.Length > options.Value.MaximumResponseBytes) throw new ManagementCustomerIdentityResponseException();
            }
            JsonDocument body;
            try { body = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 12 }); }
            catch (JsonException) { throw new ManagementCustomerIdentityResponseException(); }
            if (body.RootElement.ValueKind != JsonValueKind.Object || !body.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            { body.Dispose(); throw new ManagementCustomerIdentityResponseException(); }
            return new(body, response.StatusCode);
        }
        catch (OperationCanceledException) { throw new OperationCanceledException("Customer management request was cancelled.", ct); }
        catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or
            ManagementCustomerIdentityNotFoundException or ManagementCustomerIdentityRateLimitException or
            ManagementIdentityOperationConflictException or ManagementCustomerIdentityPreconditionException or
            ManagementCustomerIdentityResponseException or ManagementCustomerIdentityUnavailableException) { throw; }
        catch { throw new ManagementCustomerIdentityUnavailableException(); }
    });
    private static bool Text(string? value, int maximum, bool optional = false) => value is null ? optional :
        value.Length <= maximum && !value.Any(char.IsControl) && !string.IsNullOrWhiteSpace(value);
    private static void Roles(IReadOnlyList<Guid>? ids, IReadOnlyList<string>? names)
    {
        if (ids is null || names is null || ids.Count > 32 || names.Count > 32 || ids.Any(x => x == Guid.Empty) ||
            names.Any(x => !Text(x, 100)) || names.Sum(x => x.Length) > 1024) throw new ManagementCustomerIdentityResponseException();
    }
    private static T Safe<T>(T value, Guid? tenantId, Guid? id) where T : class
    {
        object safe = value switch {
            ManagementCustomerTenantDto tenant when tenant.TenantId != Guid.Empty && (id is null || id == tenant.TenantId) &&
                Text(tenant.Name, 512) && tenant.Status is ("Active" or "Disabled") && Text(tenant.Version, 256, true) && ManagementTenantSources.IsValid(tenant.Source)
                => new ManagementCustomerTenantDto(tenant.TenantId, tenant.Name, tenant.Status, tenant.CreatedUtc, tenant.UpdatedUtc, tenant.Version) { Source = tenant.Source },
            ManagementCustomerUserDto user when user.UserId != Guid.Empty && user.TenantId == tenantId && (id is null || id == user.UserId) &&
                Text(user.Email, 320, true) && Text(user.FirstName, 128, true) && Text(user.LastName, 128, true) &&
                user.Status is ("active" or "disabled") && Text(user.Version, 256, true) => Copy(user),
            ManagementCustomerInvitationDto invite when invite.InviteId != Guid.Empty && invite.TenantId == tenantId && (id is null || id == invite.InviteId) &&
                Text(invite.Email, 320, true) && Text(invite.FirstName, 128, true) && Text(invite.LastName, 128, true) &&
                invite.Status is ("pending" or "sent" or "expired" or "redeemed" or "revoked") && Text(invite.Version, 256) => Copy(invite),
            _ => throw new ManagementCustomerIdentityResponseException() };
        return (T)safe;
    }
    private static ManagementCustomerUserDto Copy(ManagementCustomerUserDto row)
    {
        Roles(row.RoleIds, row.RoleNames);
        return new(row.TenantId, row.UserId, row.Email, row.FirstName, row.LastName, row.Status, row.RoleIds.ToArray(), row.RoleNames.ToArray())
            { CreatedUtc = row.CreatedUtc, UpdatedUtc = row.UpdatedUtc, Version = row.Version };
    }
    private static ManagementCustomerInvitationDto Copy(ManagementCustomerInvitationDto row)
    {
        Roles(row.RoleIds, row.RoleNames);
        return new(row.TenantId, row.InviteId, row.Email, row.FirstName, row.LastName, row.Status, row.RoleIds.ToArray(), row.RoleNames.ToArray(),
            row.CreatedUtc, row.ExpiresUtc, row.UpdatedUtc, row.Version);
    }
}
