using System.Net;
using System.Text.RegularExpressions;
using IBeam.Management.Entities;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Services;

public sealed class ManagementApplicationService(IManagementApplicationRepository repository,
    IManagementPrincipalProvider principals, IServiceOperationExecutor operations) : IManagementApplicationService
{
    public Task<ManagementApplicationList> ListAsync(int pageSize = 50, string? continuationToken = null, CancellationToken ct = default)
        => Run("management.applications.list", null, async (org, actor, token) =>
        {
            Guid? after = null;
            if (pageSize is < 1 or > 100) throw new ManagementApplicationInvalidException();
            if (continuationToken is not null)
            {
                if (!Guid.TryParseExact(continuationToken, "N", out var parsed) || parsed == Guid.Empty)
                    throw new ManagementApplicationInvalidException();
                after = parsed;
            }
            var page = await repository.ListAsync(org, after, pageSize, token);
            if (page.Items.Count > pageSize || page.Items.Any(x => x.OrganizationId != org))
                throw new ManagementApplicationUnavailableException();
            return new ManagementApplicationList(page.Items.Select(Dto).ToArray(), pageSize, page.Next?.ToString("N"));
        }, ct);
    public Task<ManagementApplicationDto> GetAsync(Guid applicationId, CancellationToken ct = default)
        => Run("management.applications.read", applicationId, async (org, actor, token) =>
            Dto(await RequireAsync(repository, org, applicationId, token)), ct);
    public Task<ManagementApplicationDto> CreateAsync(CreateManagementApplication request, CancellationToken ct = default)
        => Run("management.applications.create", null, async (org, actor, token) =>
        {
            if (request is null || !Regex.IsMatch(request.ApplicationKey ?? "", "^[a-z0-9][a-z0-9-]{0,99}$") ||
                !Regex.IsMatch(request.Environment ?? "", "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$"))
                throw new ManagementApplicationInvalidException();
            ValidateName(request.Name);
            var url = NormalizeOrigin(request.BaseUrl);
            var now = DateTimeOffset.UtcNow;
            return Dto(await repository.CreateAsync(new()
            {
                OrganizationId = org, ApplicationId = Guid.NewGuid(), ApplicationKey = request.ApplicationKey!,
                Environment = request.Environment!, Name = request.Name.Trim(), BaseUrl = url, IsEnabled = true,
                CreatedUtc = now, UpdatedUtc = now, CreatedBy = actor, UpdatedBy = actor
            }, token));
        }, ct);
    public Task<ManagementApplicationDto> UpdateAsync(Guid applicationId, UpdateManagementApplication request, CancellationToken ct = default)
        => Run("management.applications.update", applicationId, async (org, actor, token) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.ExpectedVersion) ||
                request.ExpectedVersion.Length > 256 || request.ExpectedVersion == "*") throw new ManagementApplicationInvalidException();
            ValidateName(request.Name);
            var url = NormalizeOrigin(request.BaseUrl);
            var row = await RequireAsync(repository, org, applicationId, token);
            return Dto(await repository.ReplaceAsync(row with
            {
                Name = request.Name.Trim(), BaseUrl = url, IsEnabled = request.IsEnabled,
                ActiveCredentialId = url == row.BaseUrl ? row.ActiveCredentialId : null,
                UpdatedUtc = DateTimeOffset.UtcNow, UpdatedBy = actor
            }, request.ExpectedVersion, token));
        }, ct);

    internal static Task<(Guid OrganizationId, string Actor)> RequireAdministratorAsync(IManagementPrincipalProvider principals, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var principal = principals.GetPrincipal();
        var identity = ManagementPrincipalGuard.RequireIdentity(principal, ManagementSubjectTypes.Human, ManagementApplicationOperations.Admin);
        return Task.FromResult((ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.OrganizationId),
            ManagementPrincipalGuard.RequireHumanUserId(identity)));
    }
    internal Task<ManagementApplicationEntity> GetConnectionStateAsync(Guid id, CancellationToken ct)
        => Run("management.applications.connection.read", id, (org, actor, token) => RequireAsync(repository, org, id, token), ct);

    internal Task<ManagementApplicationEntity> ActivateConnectionAsync(ManagementApplicationEntity expected, Guid credentialId, CancellationToken ct)
        => Run("management.applications.connection.activate", expected.ApplicationId, async (org, actor, token) =>
        {
            if (expected.OrganizationId != org || !expected.IsEnabled) throw new ManagementAccessDeniedException();
            return await repository.ReplaceAsync(expected with { ActiveCredentialId = credentialId,
                UpdatedUtc = DateTimeOffset.UtcNow, UpdatedBy = actor }, expected.Version, token);
        }, ct);

    internal static async Task<ManagementApplicationEntity> RequireAsync(IManagementApplicationRepository repository, Guid org, Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ManagementApplicationInvalidException();
        var row = await repository.GetAsync(org, id, ct);
        if (row is null) throw new ManagementApplicationNotFoundException();
        if (row.OrganizationId != org || row.ApplicationId != id) throw new ManagementApplicationUnavailableException();
        return row;
    }
    internal static string NormalizeOrigin(string? value)
    {
        if (value is null || value.Length > 512 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 ||
            uri.Fragment.Length != 0 || uri.AbsolutePath != "/" || IPAddress.TryParse(uri.Host, out _) ||
            uri.HostNameType != UriHostNameType.Dns || uri.Host.EndsWith('.'))
            throw new ManagementApplicationInvalidException();
        return uri.GetLeftPart(UriPartial.Authority);
    }
    private static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl)) throw new ManagementApplicationInvalidException();
    }
    internal static ManagementApplicationDto Dto(ManagementApplicationEntity row)
        => new(row.ApplicationId, row.ApplicationKey, row.Name, row.Environment, row.BaseUrl, row.IsEnabled,
            row.CreatedUtc, row.UpdatedUtc, row.Version, row.ActiveCredentialId.HasValue);
    private async Task<T> Run<T>(string operation, Guid? id, Func<Guid, string, CancellationToken, Task<T>> action, CancellationToken ct)
    {

        return await operations.ExecuteAsync(this, async token =>
        {
            try { var (org, actor) = await RequireAdministratorAsync(principals, token); return await action(org, actor, token); }
            catch (Exception ex) when (ex is not (ManagementApplicationInvalidException or ManagementApplicationConflictException or
                ManagementApplicationNotFoundException or ManagementApplicationUnavailableException or ManagementAccessDeniedException or OperationCanceledException))
            { throw new ManagementApplicationUnavailableException(); }
        }, new ServiceOperationExecutionOptions
        {
            OperationName = operation, AuditAction = operation, EntityId = id,
            EntityName = nameof(ManagementApplicationEntity), PermissionEnabled = false, AuditEnabled = true
        }, ct);
    }
}
public static class ManagementApplicationServiceExtensions
{
    public static IServiceCollection AddIBeamManagementApplications(this IServiceCollection services)
    {
        services.AddIBeamManagementServices();
        services.TryAddScoped<ManagementApplicationService>();
        services.TryAddScoped<IManagementApplicationService>(p => p.GetRequiredService<ManagementApplicationService>());
        return services;
    }
}
