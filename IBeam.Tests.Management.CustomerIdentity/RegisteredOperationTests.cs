using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class RegisteredOperationTests
{
    private const string Operation = "management.apps.fixture.inspect";
    public sealed record Request(Guid TenantId, string Label);
    public sealed record Result(string Status);
    public sealed record OtherRequest(Guid TenantId);
    public sealed record UnsafeRequest(object Payload);
    public sealed class RecursiveRequest { public RecursiveRequest? Child { get; set; } }
    public sealed record DictionaryRequest(Dictionary<string, string> Payload);
    public sealed record JsonRequest(JsonElement Payload);
    public sealed record BytesRequest(byte[] Payload);
    public sealed class Handler : IManagementOperationHandler<Request, Result>, IManagementOperationReceiptHandler<Result>
    {
        public Task<Result> ReadReceiptAsync(ManagementOperationContext context, CancellationToken ct = default)
            => throw new AssertFailedException("Discovery must never invoke receipt lookup.");
        public Task<Result> ExecuteAsync(ManagementOperationContext context, Request request, CancellationToken ct = default)
            => throw new AssertFailedException("Discovery must never execute an app handler.");
    }
    private static ManagementOperationDescriptor Descriptor => new(Operation, "POST",
        "/api/management-operations/fixture/tenants/{tenantId:guid}/inspect",
        new(typeof(Request), "fixture.inspect.request"), new(typeof(Result), "fixture.inspect.result"),
        typeof(IManagementOperationHandler<Request, Result>), [Operation], 1, Operation);

    [TestMethod]
    public void DefaultCatalogPreservesEveryBuiltInWithoutAddingReservedDataAuthority()
    {
        var registry = ManagementOperationRegistry.BuiltIn;
        CollectionAssert.AreEquivalent(ManagementCustomerIdentityOperations.All.Concat(new[] {
            ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead, ManagementRegisteredOperations.Info }).ToArray(), registry.Operations.ToArray());
        foreach (var op in ManagementCustomerIdentityOperations.All.Where(ManagementCustomerIdentityPermissionPolicy.IsAssignable))
        {
            CollectionAssert.AreEquivalent(ManagementCustomerIdentityPermissionPolicy.RequiredFor(op).ToArray(), registry.RequiredFor(op).ToArray());
            var app = Guid.NewGuid();
            Assert.AreEqual(ManagementCustomerIdentityPermissionPolicy.DelegationFor(op, app), registry.DelegationFor(op, app));
        }
        Assert.IsFalse(registry.Contains(ManagementTenantDataOperations.Export));
        Assert.IsFalse(registry.Contains(ManagementTenantDataOperations.Delete));
        Assert.IsFalse(registry.IsAssignable(ManagementCustomerIdentityOperations.Info));
        Assert.IsFalse(registry.IsAssignable(ManagementOperations.DiagnosticsInfo));
        Assert.IsFalse(registry.ValidCapabilities([Operation]));
        Assert.HasCount(0, registry.Descriptors);
    }

    [TestMethod]
    [DataRow("wildcard")] [DataRow("namespace")] [DataRow("identity-shadow")] [DataRow("url")]
    [DataRow("route-query")] [DataRow("route-catchall")] [DataRow("route-encoded")] [DataRow("route-double-slot")]
    [DataRow("schema-version")] [DataRow("protocol-version")] [DataRow("receipt-version")]
    [DataRow("unsafe-schema")] [DataRow("recursive-schema")] [DataRow("dictionary-schema")] [DataRow("json-schema")]
    [DataRow("bytes-schema")] [DataRow("primitive-schema")] [DataRow("handler-mismatch")] [DataRow("abstract-handler")]
    [DataRow("missing-self")] [DataRow("unknown-grant")] [DataRow("duplicate-grant")] [DataRow("audit-text")]
    [DataRow("request-limit")] [DataRow("result-limit")] [DataRow("method")]
    public void UnsafeRegistrationFailsClosed(string change)
    {
        var d = Descriptor;
        Type? request = change switch { "unsafe-schema" => typeof(UnsafeRequest), "recursive-schema" => typeof(RecursiveRequest),
            "dictionary-schema" => typeof(DictionaryRequest), "json-schema" => typeof(JsonRequest), "bytes-schema" => typeof(BytesRequest),
            "primitive-schema" => typeof(string), _ => null };
        d = change switch
        {
            "wildcard" => d with { Operation = "management.apps.fixture.*" },
            "namespace" => d with { Operation = "fixture.inspect" },
            "identity-shadow" => d with { Operation = ManagementCustomerIdentityOperations.OwnersTransfer },
            "url" => d with { RouteTemplate = "https://untrusted.example/purge" },
            "route-query" => d with { RouteTemplate = d.RouteTemplate + "?target=other" },
            "route-catchall" => d with { RouteTemplate = "/api/management-operations/{**url}" },
            "route-encoded" => d with { RouteTemplate = "/api/management-operations/%2e%2e/purge" },
            "route-double-slot" => d with { RouteTemplate = "/api/management-operations/{id:guid}/{id:guid}" },
            "schema-version" => d with { Request = d.Request with { Version = 2 } },
            "protocol-version" => d with { ProtocolVersion = 2 },
            "receipt-version" => d with { ReceiptProtocolVersion = 0 },
            "handler-mismatch" => d with { HandlerServiceType = typeof(IManagementOperationHandler<OtherRequest, Result>) },
            "abstract-handler" => d with { HandlerServiceType = typeof(IDisposable) },
            "missing-self" => d with { RequiredGrants = [ManagementOperations.ErrorsRead] },
            "unknown-grant" => d with { RequiredGrants = [Operation, "management.apps.unknown.read"] },
            "duplicate-grant" => d with { RequiredGrants = [Operation, Operation] },
            "audit-text" => d with { AuditAction = "private-customer-canary" },
            "request-limit" => d with { MaximumRequestBytes = int.MaxValue },
            "result-limit" => d with { MaximumResultBytes = int.MaxValue },
            "method" => d with { Method = "CONNECT" },
            _ => d with { Request = d.Request with { ContractType = request! },
                HandlerServiceType = typeof(IManagementOperationHandler<,>).MakeGenericType(request!, typeof(Result)) }
        };
        Assert.Throws<ArgumentException>(() => new ManagementOperationRegistry([d]));
    }

    [TestMethod]
    public void DuplicateNamesOverlappingRoutesAndAmbiguousHandlerBindingsAreRejected()
    {
        var d = Descriptor;
        var other = d with { Operation = "management.apps.fixture.other", RequiredGrants = ["management.apps.fixture.other"],
            AuditAction = "management.apps.fixture.other", Request = new(typeof(OtherRequest), "fixture.other.request"),
            HandlerServiceType = typeof(IManagementOperationHandler<OtherRequest, Result>) };
        Assert.Throws<ArgumentException>(() => new ManagementOperationRegistry([d, d]));
        Assert.Throws<ArgumentException>(() => new ManagementOperationRegistry([d with { Result = d.Result with { Id = d.Request.Id } }]));
        Assert.Throws<ArgumentException>(() => new ManagementOperationRegistry([d, other with {
            RouteTemplate = d.RouteTemplate.Replace("tenantId", "differentId") }]));
        Assert.Throws<ArgumentException>(() => new ManagementOperationRegistry([d, other with {
            RouteTemplate = "/api/management-operations/other", Request = d.Request, HandlerServiceType = d.HandlerServiceType }]));
        var cycle = other with { RouteTemplate = "/api/management-operations/other", RequiredGrants = [other.Operation, Operation] };
        Assert.Throws<ArgumentException>(() => new ManagementOperationRegistry([d with { RequiredGrants = [Operation, other.Operation] }, cycle]));
    }

    [TestMethod]
    public void RegistryCopiesMutableInputAndUsesOneExplicitGrantClosure()
    {
        var grants = new List<string> { Operation, ManagementCustomerIdentityOperations.OnboardingCreate };
        var descriptors = new List<ManagementOperationDescriptor> { Descriptor with { RequiredGrants = grants } };
        var registry = new ManagementOperationRegistry(descriptors);
        grants.Clear(); descriptors.Clear();
        Assert.HasCount(5, registry.RequiredFor(Operation));
        Assert.HasCount(2, registry.Find(Operation)!.RequiredGrants);
        Assert.HasCount(0, registry.Capabilities([Operation], [Operation], _ => true));
        var required = registry.RequiredFor(Operation);
        Assert.HasCount(1, registry.Capabilities(required, required, _ => true));
        Assert.HasCount(0, registry.Capabilities(required, required, _ => false));
        Assert.Throws<ManagementAccessDeniedException>(() => registry.Capabilities([Operation, Operation], [Operation], _ => true));
        Assert.IsTrue(registry.AllowsAudit(Operation));
        Assert.IsFalse(registry.AllowsAudit(Operation + ".private-canary"));
    }

    [TestMethod]
    [DataRow("method")] [DataRow("route")] [DataRow("scope")] [DataRow("encoded")] [DataRow("query")] [DataRow("size")]
    public void SignedExtensionBindingRejectsCallerRouteAndPayloadSubstitution(string change)
    {
        var registry = new ManagementOperationRegistry([Descriptor]);
        var path = Descriptor.RouteTemplate.Replace("{tenantId:guid}", Guid.NewGuid().ToString("D"));
        var valid = new ManagementDelegationRequest { Method = "POST", PathAndQuery = path, Body = "{}"u8.ToArray(), IdempotencyKey = "intent-1234", IfMatch = "etag-1" };
        Assert.AreEqual(64, ManagementDelegationBinding.Hash(valid, Operation, registry).Length);
        var changed = new ManagementDelegationRequest { Method = change == "method" ? "DELETE" : valid.Method,
            PathAndQuery = change switch { "route" => "https://attacker.example/delete", "scope" => path.Replace("/tenants/", "/users/"),
                "encoded" => path.Replace("inspect", "%69nspect"), "query" => path + "?url=other", _ => path },
            Body = change == "size" ? new byte[Descriptor.MaximumRequestBytes + 1] : valid.Body,
            IdempotencyKey = valid.IdempotencyKey, IfMatch = valid.IfMatch };
        Assert.Throws<ManagementAccessDeniedException>(() => ManagementDelegationBinding.Hash(changed, Operation, registry));
        Assert.Throws<ManagementAccessDeniedException>(() => ManagementDelegationBinding.Hash(valid));
    }

    [TestMethod]
    public void TypedPayloadCodecRejectsUnknownDuplicateWrongTypeAndOversizedPayloads()
    {
        var codec = new ManagementOperationPayloadCodec(new([Descriptor with { MaximumResultBytes = 64 }]));
        var input = new Request(Guid.NewGuid(), "synthetic");
        Assert.AreEqual(input, codec.ReadRequest<Request>(Operation, JsonSerializer.SerializeToUtf8Bytes(input, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        foreach (var json in new[] { "null", "{\"label\":\"one\",\"label\":\"two\"}", "{\"label\":\"one\",\"Label\":\"two\"}", "{\"url\":\"https://other.example\"}", "{\"tenantId\":\"invalid\"}" })
            Assert.Throws<ManagementAccessDeniedException>(() => codec.ReadRequest<Request>(Operation, Encoding.UTF8.GetBytes(json)));
        Assert.Throws<ManagementAccessDeniedException>(() => codec.ReadRequest<Result>(Operation, "{}"u8.ToArray()));
        Assert.Throws<ManagementAccessDeniedException>(() => codec.ReadRequest<Request>(Operation, new byte[32769]));
        Assert.Throws<ManagementAccessDeniedException>(() => codec.WriteResult(Operation, new Result(new string('x', 100))));
        Assert.Throws<ManagementAccessDeniedException>(() => codec.ReadResult<Result>(Operation, "{\"extra\":true}"u8.ToArray()));
        Assert.AreEqual(new Result("ready"), codec.ReadResult<Result>(Operation, codec.WriteResult(Operation, new Result("ready"))));
    }

    [TestMethod]
    [DataRow("missing-handler")] [DataRow("disabled")] [DataRow("ungranted")] [DataRow("revoked")]
    [DataRow("expired")] [DataRow("stale-version")] [DataRow("wrong-app")] [DataRow("human")]
    [DataRow("valid")]
    public async Task DiscoveryRequiresAnInstalledHandlerExplicitEnablementAndLiveMachineCredential(string change)
    {
        var registry = new ManagementOperationRegistry([Descriptor]);
        var id = Guid.NewGuid();
        var principal = new Mock<IManagementPrincipalProvider>();
        principal.Setup(x => x.GetPrincipal()).Returns(new ClaimsPrincipal(new ClaimsIdentity([
            new(ManagementClaimTypes.SubjectType, change == "human" ? ManagementSubjectTypes.Human : ManagementSubjectTypes.Credential),
            new(ManagementClaimTypes.Permission, ManagementOperations.DiagnosticsInfo),
            new(ManagementClaimTypes.CredentialId, id.ToString("D")), new(ManagementClaimTypes.CredentialVersion, "v1"),
            new(ManagementClaimTypes.ApplicationKey, "fixture"), new(ManagementClaimTypes.Environment, "Testing") ], "machine")));
        var row = new ManagementApiCredentialEntity { CredentialId = id, Version = change == "stale-version" ? "v2" : "v1",
            ApplicationKey = change == "wrong-app" ? "other" : "fixture", Environment = "Testing",
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(change == "expired" ? -1 : 1),
            RevokedUtc = change == "revoked" ? DateTimeOffset.UtcNow : null,
            Capabilities = change == "ungranted" ? [ManagementOperations.DiagnosticsInfo] : [ManagementOperations.DiagnosticsInfo, Operation] };
        var credentials = new Mock<IManagementApiCredentialRepository>();
        credentials.Setup(x => x.GetAsync("fixture", "Testing", id, It.IsAny<CancellationToken>())).ReturnsAsync(() => row);
        var services = new ServiceCollection();
        if (change != "missing-handler") services.AddScoped<IManagementOperationHandler<Request, Result>, Handler>();
        using var provider = services.BuildServiceProvider();
        var service = new ManagementOperationCapabilityService(registry, principal.Object, credentials.Object,
            Options.Create(new ManagementDiagnosticsOptions { ApplicationKey = "fixture", Environment = "Testing" }),
            Options.Create(new ManagementOperationTargetOptions { EnabledOperations = change == "disabled" ? [] : [Operation] }), provider, new ServiceOperationExecutor());
        if (change is "revoked" or "expired" or "stale-version" or "wrong-app" or "human")
            await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.GetAsync());
        else Assert.HasCount(change == "valid" ? 1 : 0, await service.GetAsync());
        // The next call reads durable state again; prior discovery never caches authority.
        row = row with { RevokedUtc = DateTimeOffset.UtcNow };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.GetAsync());
    }

    [TestMethod]
    public void DiRegistrationUsesOneImmutableCatalogAndDoesNotChangeLegacyIdentityDiscovery()
    {
        var services = new ServiceCollection();
        services.AddIBeamManagementOperation<Request, Result, Handler>(Descriptor);
        services.AddIBeamManagementServices();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ManagementOperationRegistry>();
        Assert.AreSame(registry, provider.GetRequiredService<ManagementOperationRegistry>());
        Assert.HasCount(1, registry.Capabilities([Operation], [Operation], type => provider.GetService(type) is not null));
        Assert.Throws<ManagementConnectionInvalidException>(() => ManagementCustomerIdentityProtocol.Validate(DiscoveryProtocolTests.Info with { Capabilities = [ManagementCustomerIdentityOperations.Info, Operation] }));
        var duplicate = new ServiceCollection();
        duplicate.AddIBeamManagementOperation<Request, Result, Handler>(Descriptor);
        duplicate.AddIBeamManagementOperation<Request, Result, Handler>(Descriptor);
        using var invalidProvider = duplicate.BuildServiceProvider();
        Assert.Throws<ArgumentException>(() => invalidProvider.GetRequiredService<ManagementOperationRegistry>());
    }

    [TestMethod]
    public async Task RegisteredNamesRequireExplicitAppScopedCeilingsAndLiveHumanAssignments()
    {
        var registry = new ManagementOperationRegistry([Descriptor]);
        var org = Guid.NewGuid(); var app = Guid.NewGuid(); var human = Guid.NewGuid();
        var principal = new Mock<IManagementPrincipalProvider>();
        var ceiling = false;
        principal.Setup(x => x.GetPrincipal()).Returns(() => new(new ClaimsIdentity(new[] {
            new Claim("sub", human.ToString("D")), new(ManagementClaimTypes.OrganizationId, org.ToString("D")),
            new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human),
            new(ManagementClaimTypes.Permission, ManagementOperations.IdentityRead),
            new(ManagementClaimTypes.Permission, ManagementSupportOperations.AccessAdmin)
        }.Concat(ceiling ? new[] { new Claim(ManagementClaimTypes.Permission, registry.DelegationFor(Operation, app)) } : []), "support")));
        var user = new ManagementSupportUserState(org, human, true, true);
        var users = new Mock<IManagementSupportUserStateProvider>();
        users.Setup(x => x.GetAsync(org, human, It.IsAny<CancellationToken>())).ReturnsAsync(() => user);
        var application = new ManagementApplicationEntity { OrganizationId = org, ApplicationId = app };
        var apps = new Mock<IManagementApplicationStateProvider>();
        apps.Setup(x => x.GetAsync(org, app, It.IsAny<CancellationToken>())).ReturnsAsync(() => application);
        ManagementApplicationAccessEntity? assignment = null;
        var assignments = new Mock<IManagementApplicationAccessRepository>();
        assignments.Setup(x => x.GetAsync(org, app, human, It.IsAny<CancellationToken>())).ReturnsAsync(() => assignment);
        assignments.Setup(x => x.CreateAsync(It.IsAny<ManagementApplicationAccessEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagementApplicationAccessEntity value, CancellationToken _) => assignment = value with { Version = "v1" });
        var service = new ManagementApplicationAccessService(assignments.Object, apps.Object, users.Object, principal.Object, new ServiceOperationExecutor(), registry);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.RequireAsync(app, Operation));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.PutAsync(app, human, new() { Permissions = [Operation] }));
        ceiling = true;
        await service.PutAsync(app, human, new() { Permissions = [Operation] });
        await service.RequireAsync(app, Operation);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.RequireAsync(Guid.NewGuid(), Operation));
        assignment = assignment! with { RevokedUtc = DateTimeOffset.UtcNow };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.RequireAsync(app, Operation));
        assignment = assignment with { RevokedUtc = null }; user = user with { IsEnabled = false };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.RequireAsync(app, Operation));
        user = user with { IsEnabled = true }; application = application with { IsEnabled = false };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.RequireAsync(app, Operation));
    }

    [TestMethod]
    public async Task CredentialIssuanceAndValidationUseSameRegistryWithoutExpandingExistingGrants()
    {
        var registry = new ManagementOperationRegistry([Descriptor]);
        ManagementApiCredentialEntity? row = null;
        var credentials = new Mock<IManagementApiCredentialRepository>();
        credentials.Setup(x => x.CreateAsync(It.IsAny<ManagementApiCredentialEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagementApiCredentialEntity value, CancellationToken _) => row = value with { Version = "v1" });
        credentials.Setup(x => x.GetAsync("fixture", "Testing", It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => row);
        var authorized = new Mock<IManagementCredentialOperatorProvider>();
        authorized.Setup(x => x.RequireAuthorizedOperatorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        var deployment = Options.Create(new ManagementDiagnosticsOptions { ApplicationKey = "fixture", Environment = "Testing" });
        var codec = new ManagementCredentialSecretCodec();
        var provisioning = new ManagementCredentialProvisioningService(credentials.Object, authorized.Object, codec, deployment,
            Options.Create(new ManagementCredentialProvisioningOptions { Enabled = true }), new ServiceOperationExecutor(), registry);
        var validator = new ManagementCredentialValidator(credentials.Object, codec, deployment, new ServiceOperationExecutor(), registry);
        var old = await provisioning.IssueAsync([ManagementOperations.DiagnosticsInfo], DateTimeOffset.UtcNow.AddHours(1));
        Assert.HasCount(1, (await validator.ValidateAsync(old.ApiKey))!.Capabilities);
        Assert.DoesNotContain(Operation, row!.Capabilities);
        var added = await provisioning.IssueAsync([ManagementOperations.DiagnosticsInfo, Operation], DateTimeOffset.UtcNow.AddHours(1));
        Assert.Contains(Operation, (await validator.ValidateAsync(added.ApiKey))!.Capabilities);
        await Assert.ThrowsAsync<ArgumentException>(() => provisioning.IssueAsync(["management.apps.fixture.*"], DateTimeOffset.UtcNow.AddHours(1)));
        row = row! with { RevokedUtc = DateTimeOffset.UtcNow };
        Assert.IsNull(await validator.ValidateAsync(added.ApiKey));
        var legacy = new ManagementCredentialProvisioningService(credentials.Object, authorized.Object, codec, deployment,
            Options.Create(new ManagementCredentialProvisioningOptions { Enabled = true }), new ServiceOperationExecutor());
        await Assert.ThrowsAsync<ArgumentException>(() => legacy.IssueAsync([Operation], DateTimeOffset.UtcNow.AddHours(1)));
    }

    [TestMethod]
    public async Task ExtensionAuditUsesCatalogAllowlistAndNeverCopiesPayloadsOrProviderErrors()
    {
        var entries = new List<ManagementAuditEntity>();
        var repository = new Mock<IManagementAuditRepository>();
        repository.Setup(x => x.AppendAsync(It.IsAny<ManagementAuditEntity>(), It.IsAny<CancellationToken>()))
            .Callback<ManagementAuditEntity, CancellationToken>((value, _) => entries.Add(value)).Returns(Task.CompletedTask);
        var principals = new Mock<IManagementPrincipalProvider>();
        principals.Setup(x => x.GetPrincipal()).Returns(new ClaimsPrincipal());
        var sink = new ManagementAuditSink(repository.Object, principals.Object, new ManagementAuditContext(),
            Options.Create(new ManagementAuditOptions()), Options.Create(new ManagementDiagnosticsOptions()), TimeProvider.System,
            new ManagementOperationRegistry([Descriptor]));
        var transaction = new ServiceAuditTransaction { Action = Operation, OriginalJson = "PRIVATE-CANARY",
            TransformedJson = "PRIVATE-CANARY", ActorId = "PRIVATE-CANARY", IpAddress = "PRIVATE-CANARY", Succeeded = false };
        await sink.WriteTransactionAsync(transaction);
        Assert.HasCount(1, entries);
        Assert.DoesNotContain("PRIVATE-CANARY", JsonSerializer.Serialize(entries));
        transaction.Action = Operation + ".PRIVATE-CANARY";
        await sink.WriteTransactionAsync(transaction);
        Assert.HasCount(1, entries);
    }
}
