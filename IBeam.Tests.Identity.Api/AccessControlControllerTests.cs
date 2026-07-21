using System.Security.Claims;
using IBeam.AccessControl;
using IBeam.Identity.Api.Controllers;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using AccessControlSubjectTypes = IBeam.AccessControl.AccessSubjectTypes;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class AccessControlControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("b3e596c8-277a-5e0e-8d83-d32ccd9f1129");
    private static readonly Guid GrantId = Guid.Parse("326bb9b2-8a81-48a1-b550-ceb84de7891f");
    private static readonly Guid UserId = Guid.Parse("81cd7b8c-fe7a-5843-ae24-4284e6f5bc3e");

    [TestMethod]
    public async Task DeleteGrant_ReturnsNoContent_AndDefaultListExcludesRevokedGrant()
    {
        var resourceAccess = new FakeResourceAccessService();
        resourceAccess.Grants.Add(ActiveGrant());
        var sut = CreateController(resourceAccess);

        var deleteResult = await sut.DeleteGrant(TenantId, GrantId, CancellationToken.None);
        var listResult = await sut.GetGrants(
            TenantId,
            resourceType: null,
            resourceId: null,
            subjectType: AccessControlSubjectTypes.User,
            subjectId: UserId.ToString("D"),
            includeRevoked: false,
            includeInactive: false,
            CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(deleteResult);
        Assert.IsInstanceOfType<OkObjectResult>(listResult);
        Assert.HasCount(0, (IReadOnlyList<ResourceAccessGrantInfo>)((OkObjectResult)listResult).Value!);
    }

    [TestMethod]
    public async Task GetGrants_CanIncludeRevokedGrant_ForAuditViews()
    {
        var resourceAccess = new FakeResourceAccessService();
        resourceAccess.Grants.Add(ActiveGrant() with { Status = ResourceAccessGrantStatuses.Revoked });
        var sut = CreateController(resourceAccess);

        var result = await sut.GetGrants(
            TenantId,
            resourceType: "project",
            resourceId: "project-1",
            subjectType: AccessControlSubjectTypes.User,
            subjectId: UserId.ToString("D"),
            includeRevoked: true,
            includeInactive: false,
            CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        var grants = (IReadOnlyList<ResourceAccessGrantInfo>)((OkObjectResult)result).Value!;
        Assert.HasCount(1, grants);
        Assert.AreEqual(ResourceAccessGrantStatuses.Revoked, grants[0].Status);
        Assert.AreEqual("project", resourceAccess.LastResourceType);
        Assert.AreEqual("project-1", resourceAccess.LastResourceId);
        Assert.IsTrue(resourceAccess.LastIncludeInactive);
    }

    private static AccessControlController CreateController(FakeResourceAccessService resourceAccess)
    {
        var controller = new AccessControlController(
            new FakeIBeamAccessControlService(),
            new FakeApiCredentialAccessService(),
            new FakeApiCredentialService(),
            resourceAccess,
            new StaticOptionsSnapshot<IBeamAccessControlOptions>(new IBeamAccessControlOptions()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("tid", TenantId.ToString("D")),
                        new Claim("uid", UserId.ToString("D")),
                        new Claim("role", "admin")
                    ], "unit-test"))
                }
            }
        };

        return controller;
    }

    private static ResourceAccessGrantInfo ActiveGrant()
        => new(
            GrantId,
            TenantId,
            "project",
            "project-1",
            new AccessSubject(AccessControlSubjectTypes.User, UserId.ToString("D")),
            ResourceAccessLevels.View,
            ResourceAccessGrantStatuses.Active,
            DateTimeOffset.UtcNow,
            UserId,
            null,
            null,
            new Dictionary<string, string>());

    private sealed class FakeResourceAccessService : IResourceAccessService
    {
        public List<ResourceAccessGrantInfo> Grants { get; } = [];
        public string? LastResourceType { get; private set; }
        public string? LastResourceId { get; private set; }
        public bool LastIncludeInactive { get; private set; }

        public Task<IReadOnlyList<ResourceAccessGrantInfo>> ListGrantsAsync(
            Guid tenantId,
            string? resourceType = null,
            string? resourceId = null,
            AccessSubject? subject = null,
            CancellationToken ct = default,
            bool includeInactive = false)
        {
            LastResourceType = resourceType;
            LastResourceId = resourceId;
            LastIncludeInactive = includeInactive;

            var query = Grants
                .Where(x => x.TenantId == tenantId)
                .Where(x => includeInactive || string.Equals(x.Status, ResourceAccessGrantStatuses.Active, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(resourceType))
                query = query.Where(x => string.Equals(x.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(resourceId))
                query = query.Where(x => string.Equals(x.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));

            if (subject is not null)
            {
                query = query.Where(x =>
                    string.Equals(x.Subject.SubjectType, subject.SubjectType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Subject.SubjectId, subject.SubjectId, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<ResourceAccessGrantInfo>>(query.ToList());
        }

        public Task<ResourceAccessGrantInfo> GrantAccessAsync(Guid tenantId, GrantResourceAccessRequest request, Guid? createdByUserId = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ResourceAccessGrantInfo> UpdateGrantAsync(Guid tenantId, Guid grantId, UpdateResourceAccessGrantRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RevokeGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
        {
            var index = Grants.FindIndex(x => x.TenantId == tenantId && x.GrantId == grantId);
            if (index >= 0)
            {
                Grants[index] = Grants[index] with
                {
                    Status = ResourceAccessGrantStatuses.Revoked,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeIBeamAccessControlService : IIBeamAccessControlService
    {
        public Task<bool> HasRoleAsync(ClaimsPrincipal principal, string roleName, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> HasPermissionAsync(ClaimsPrincipal principal, string permissionName, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> HasModuleAccessAsync(ClaimsPrincipal principal, string moduleKey, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> HasResourceAccessAsync(ClaimsPrincipal principal, string resourceType, string resourceId, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default) => Task.FromResult(false);
        public Task RequirePermissionAsync(ClaimsPrincipal principal, string permissionName, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireModuleAccessAsync(ClaimsPrincipal principal, string moduleKey, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireResourceAccessAsync(ClaimsPrincipal principal, string resourceType, string resourceId, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default) => Task.CompletedTask;
        public Task<AccessCatalogDto> GetAccessCatalogAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccessOperationCatalogItem>> GetOperationCatalogAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccessCatalogOverride>> GetAccessCatalogOverridesAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AccessCatalogOverride> UpsertAccessCatalogOverrideAsync(Guid tenantId, Guid? catalogItemId, UpsertAccessCatalogOverrideRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAccessCatalogOverrideAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<AccessContextDto> GetCurrentAccessContextAsync(ClaimsPrincipal principal, Guid? tenantId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AccessDecision> CheckAccessAsync(ClaimsPrincipal principal, Guid tenantId, AccessCheckRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeApiCredentialAccessService : IApiCredentialAccessService
    {
        public Task<ApiCredentialAccessContextDto> BuildAccessContextAsync(ApiCredentialInfo credential, string? requestedAgentKey = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiCredentialContext?> GetCurrentApiCredentialAsync(ClaimsPrincipal principal, CancellationToken ct = default) => Task.FromResult<ApiCredentialContext?>(null);
        public Task<ApiCredentialAccessContextDto> GetCurrentAccessContextAsync(ClaimsPrincipal principal, string? requestedAgentKey = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasApiScopeAsync(ClaimsPrincipal principal, string moduleKey, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> HasToolAccessAsync(ClaimsPrincipal principal, string toolKey, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CanActAsAgentAsync(ClaimsPrincipal principal, string agentKey, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CanCredentialActAsAgentAsync(Guid tenantId, Guid credentialId, string agentKey, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> HasResourceAccessAsync(ClaimsPrincipal principal, string resourceType, string resourceId, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default) => Task.FromResult(false);
        public Task RequireApiScopeAsync(ClaimsPrincipal principal, string moduleKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireToolAccessAsync(ClaimsPrincipal principal, string toolKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireAgentAccessAsync(ClaimsPrincipal principal, string agentKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireResourceAccessAsync(ClaimsPrincipal principal, string resourceType, string resourceId, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeApiCredentialService : IApiCredentialService
    {
        public Task<CreateApiCredentialResult> CreateAsync(Guid tenantId, CreateApiCredentialRequest request, Guid? createdByUserId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ApiCredentialInfo>> ListAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiCredentialInfo> GetAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiCredentialInfo> UpdateAsync(Guid tenantId, Guid credentialId, UpdateApiCredentialRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiCredentialInfo> UpdateRolesAsync(Guid tenantId, Guid credentialId, UpdateApiCredentialRolesRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiCredentialAccessContextDto> GetAccessAsync(Guid tenantId, Guid credentialId, string? requestedAgentKey = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiCredentialAccessContextDto> UpdateAccessAsync(Guid tenantId, Guid credentialId, UpdateApiCredentialAccessRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CreateApiCredentialResult> RotateAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiCredentialInfo> RevokeAsync(Guid tenantId, Guid credentialId, Guid? revokedByUserId, string? reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApiCredentialInfo> ActivateAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StaticOptionsSnapshot<T> : IOptionsSnapshot<T>
        where T : class
    {
        public StaticOptionsSnapshot(T value) => Value = value;
        public T Value { get; }
        public T Get(string? name) => Value;
    }
}
