using IBeam.Management.Entities;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services;

/// <summary>Coordinates credential metadata with the application service; activation is a single ETag-protected app pointer change.</summary>
public sealed class ManagementApplicationConnectionService(ManagementApplicationService applications,
    IManagementApplicationCredentialRepository credentials, IManagementSecretStore secrets,
    IManagementTargetConnectionProbe probe, IManagementPrincipalProvider principals, IServiceOperationExecutor operations,
    IOptions<ManagementConnectionOptions> options, IOptions<ManagementEnvironmentSecretOptions> fixedSecrets)
    : IManagementApplicationConnectionService
{
    private readonly IManagementTargetProfileProbe? _profileProbe;
    // Preserve the original public constructor for existing package consumers.
    public ManagementApplicationConnectionService(ManagementApplicationService applications,
        IManagementApplicationCredentialRepository credentials, IManagementSecretStore secrets,
        IManagementTargetConnectionProbe probe, IManagementPrincipalProvider principals, IServiceOperationExecutor operations,
        IOptions<ManagementConnectionOptions> options, IOptions<ManagementEnvironmentSecretOptions> fixedSecrets,
        IManagementTargetProfileProbe profileProbe)
        : this(applications, credentials, secrets, probe, principals, operations, options, fixedSecrets) => _profileProbe = profileProbe;
    public Task<ManagementConnectionResult> PutAsync(Guid applicationId, UpdateManagementConnection request, CancellationToken ct = default)
        => Run("management.applications.connection.replace", applicationId, async token =>
        {
            if (!secrets.SupportsWrites || request is null || !ValidKey(request.ApiKey)) throw new ManagementConnectionInvalidException();
            return await ReplaceAsync(applicationId, request.ExpectedVersion, async (scope, cancellation) =>
                await secrets.CreateAsync(scope, new ManagementSecretValue(request.ApiKey), cancellation), token);
        }, ct);

    /// <summary>Trusted host tooling only; resolves the configured app binding, never accepts a reference from a portal.</summary>
    public Task<ManagementConnectionResult> ActivateEnvironmentReferenceAsync(Guid applicationId, string expectedVersion, CancellationToken ct = default)
        => Run("management.applications.connection.configure", applicationId, async token =>
        {
            if (secrets.SupportsWrites) throw new ManagementConnectionInvalidException();
            return await ReplaceAsync(applicationId, expectedVersion, (scope, _) =>
            {
                var bindings = fixedSecrets.Value.Bindings.Where(x => x.OrganizationId == scope.OrganizationId && x.ApplicationId == scope.ApplicationId).ToArray();
                if (bindings.Length != 1) throw new ManagementConnectionInvalidException();
                return Task.FromResult(bindings[0].Reference);
            }, token);
        }, ct);

    private async Task<ManagementConnectionResult> ReplaceAsync(Guid applicationId, string version,
        Func<ManagementSecretScope, CancellationToken, Task<string>> referenceFactory, CancellationToken ct)
    {
        var (org, actor) = await ManagementApplicationService.RequireAdministratorAsync(principals, ct);
        var application = await applications.GetConnectionStateAsync(applicationId, ct);
        if (string.IsNullOrWhiteSpace(version) || version.Length > 256 || version == "*") throw new ManagementConnectionInvalidException();
        if (application.Version != version) throw new ManagementApplicationConflictException();
        if (!application.IsEnabled) throw new ManagementAccessDeniedException();
        var scope = new ManagementSecretScope(org, applicationId);
        var reference = await referenceFactory(scope, ct);
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 1024) throw new ManagementSecretStoreUnavailableException();
        var key = await secrets.ReadAsync(scope, reference, ct);
        if (!ValidKey(key.Value)) throw new ManagementConnectionInvalidException();
        var info = await VerifyAsync(application, key, ct);
        var credential = new ManagementApplicationCredentialEntity
        {
            OrganizationId = org, ApplicationId = applicationId, CredentialId = Guid.NewGuid(),
            SecretReference = reference, BaseUrl = application.BaseUrl, VerifiedUtc = DateTimeOffset.UtcNow,
            CreatedBy = actor, Capabilities = info.Capabilities.ToArray(), CustomerIdentity = _profileProbe is null ? null : info.CustomerIdentity
        };
        await credentials.CreateAsync(credential, ct);
        // A concurrent origin change, disable or rotation wins; the previous active pointer is never overwritten blindly.
        var updated = await applications.ActivateConnectionAsync(application, credential.CredentialId, ct);
        return new(applicationId, updated.Version, credential.VerifiedUtc, credential.Capabilities) { CustomerIdentity = credential.CustomerIdentity };
    }

    public Task<ManagementConnectionResult> TestAsync(Guid applicationId, CancellationToken ct = default)
        => Run("management.applications.connection.test", applicationId, async token =>
        {
            var (org, _) = await ManagementApplicationService.RequireAdministratorAsync(principals, token);
            var app = await applications.GetConnectionStateAsync(applicationId, token);
            if (!app.IsEnabled) throw new ManagementAccessDeniedException();
            if (app.ActiveCredentialId is not Guid id) throw new ManagementConnectionInvalidException();
            var credential = await credentials.GetAsync(org, applicationId, id, token);
            if (credential is null || credential.OrganizationId != org || credential.ApplicationId != applicationId ||
                credential.CredentialId != id || credential.BaseUrl != app.BaseUrl) throw new ManagementConnectionInvalidException();
            var key = await secrets.ReadAsync(new(org, applicationId), credential.SecretReference, token);
            if (!ValidKey(key.Value)) throw new ManagementConnectionInvalidException();
            var info = await VerifyAsync(app, key, token);
            var current = await applications.GetConnectionStateAsync(applicationId, token);
            if (current.Version != app.Version) throw new ManagementApplicationConflictException();
            return new ManagementConnectionResult(applicationId, app.Version, DateTimeOffset.UtcNow, info.Capabilities.ToArray())
                { CustomerIdentity = _profileProbe is null ? null : info.CustomerIdentity };
        }, ct);

    private async Task<ManagementTargetProfile> VerifyAsync(ManagementApplicationEntity application, ManagementSecretValue key, CancellationToken ct)
    {
        var info = _profileProbe is null
            ? new ManagementTargetProfile(await probe.ProbeAsync(application.BaseUrl, key, ct), new(ManagementCustomerIdentityStatuses.NotInstalled))
            : await _profileProbe.ProbeProfileAsync(application.BaseUrl, key, ct);
        ManagementCustomerIdentityProtocol.ValidateProfile(info, application.ApplicationKey, application.Environment);
        return info;
    }
    private static bool ValidKey(string? value)
        => value is { Length: >= 16 and <= 4096 } && value.All(c => c is >= '!' and <= '~');
    private Task<T> Run<T>(string name, Guid app, Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => operations.ExecuteAsync(this, async token =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            try
            {
                await ManagementApplicationService.RequireAdministratorAsync(principals, timeout.Token);
                return await action(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException("The management connection was cancelled.", ct);
                throw new ManagementConnectionTimeoutException();
            }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementApplicationInvalidException or
                ManagementApplicationNotFoundException or ManagementApplicationConflictException or ManagementConnectionInvalidException or
                ManagementConnectionTimeoutException or ManagementConnectionUnavailableException))
            { throw new ManagementConnectionUnavailableException(); }
        }, new ServiceOperationExecutionOptions
        {
            OperationName = name, AuditAction = name, EntityId = app, EntityName = nameof(ManagementApplicationCredentialEntity),
            PermissionEnabled = false, AuditEnabled = true
        }, ct);
}
public static class ManagementConnectionServiceExtensions
{
    public static IServiceCollection AddIBeamManagementConnections(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementApplications();
        services.AddOptions<ManagementEnvironmentSecretOptions>();
        services.AddOptions<ManagementConnectionOptions>().Bind(configuration.GetSection(ManagementConnectionOptions.SectionName))
            .Validate(x => x.TimeoutSeconds is >= 1 and <= 30 && x.ApprovedOrigins is { Length: > 0 and <= 100 } &&
                ValidOrigins(x.ApprovedOrigins), "Explicit approved HTTPS origins and a timeout of 1–30 seconds are required.").ValidateOnStart();
        services.TryAddSingleton<IManagementTargetDnsResolver, ManagementTargetDnsResolver>();
        services.TryAddSingleton<IManagementTargetSocketConnector, ManagementTargetSocketConnector>();
        services.TryAddSingleton<ManagementConnectionNetworkPolicy>();
        services.TryAddScoped<IManagementTargetConnectionProbe, ManagementTargetConnectionProbe>();
        services.TryAddScoped<ManagementApplicationConnectionService>();
        services.TryAddScoped<IManagementApplicationConnectionService>(p => p.GetRequiredService<ManagementApplicationConnectionService>());
        return services;
    }
    private static bool ValidOrigins(IEnumerable<string> origins)
    {
        try { foreach (var origin in origins) _ = ManagementApplicationService.NormalizeOrigin(origin); return true; }
        catch { return false; }
    }
}
