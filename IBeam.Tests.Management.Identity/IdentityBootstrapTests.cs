using IBeam.AccessControl;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management;
using IBeam.Management.Identity.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.Identity;

[TestClass]
public sealed class IdentityBootstrapTests
{
    [TestMethod]
    public async Task BootstrapIsOptInAndNeverTouchesStoresWhenDisabled()
    {
        var tenants = new Mock<IIdentityTenantService>(MockBehavior.Strict);
        var service = Create(new ManagementIdentityOptions { OrganizationId = Guid.NewGuid() }, tenants.Object,
            Mock.Of<IIdentityUserStore>(), Mock.Of<ITenantRoleService>(), Mock.Of<ITenantMembershipStore>());
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.BootstrapAsync(new()));
        tenants.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task SuccessfulBootstrapIsIdempotentAndDoesNotResetExistingPassword()
    {
        var organizationId = Guid.NewGuid();
        var user = new IdentityUser(Guid.NewGuid(), "support@example.test", false);
        var organization = new IdentityTenant(organizationId, "Support");
        var role = new TenantRole(organizationId, Guid.NewGuid(), ManagementIdentityRoles.Administrator, false, true, DateTimeOffset.UtcNow);
        var tenants = new Mock<IIdentityTenantService>();
        tenants.Setup(x => x.FindByIdAsync(organizationId, It.IsAny<CancellationToken>())).ReturnsAsync(organization);
        var users = new Mock<IIdentityUserStore>();
        users.SetupSequence(x => x.FindByEmailAsync(user.Email!, It.IsAny<CancellationToken>())).ReturnsAsync((IdentityUser?)null).ReturnsAsync(user with { TwoFactorEnabled = true });
        users.Setup(x => x.CreateAsync(It.IsAny<RegisterUserRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(CreateUserResult.Success(user));
        var roles = new Mock<ITenantRoleService>();
        roles.Setup(x => x.GetRolesAsync(organizationId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { role });
        var memberships = new Mock<ITenantMembershipStore>();
        memberships.SetupSequence(x => x.GetTenantForUserAsync(user.UserId, organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantInfo?)null).ReturnsAsync(new TenantInfo(organizationId, "Support", [ManagementIdentityRoles.Administrator], true));
        var service = Create(new() { OrganizationId = organizationId, BootstrapEnabled = true }, tenants.Object, users.Object, roles.Object, memberships.Object);
        var request = new ManagementBootstrapRequest { OrganizationName = "Support", AdministratorEmail = user.Email!, InitialPassword = Guid.NewGuid().ToString() };
        var first = await service.BootstrapAsync(request);
        var second = await service.BootstrapAsync(request);
        Assert.IsTrue(first.CreatedUser);
        Assert.IsFalse(second.CreatedUser);
        Assert.AreEqual(first.AdministratorUserId, second.AdministratorUserId);
        users.Verify(x => x.CreateAsync(It.IsAny<RegisterUserRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        users.Verify(x => x.SetPasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        users.Verify(x => x.SetTwoFactorAsync(user.UserId, true, "email", It.IsAny<CancellationToken>()), Times.Once);
        roles.Verify(x => x.EnsureTenantMembershipAndGrantRolesAsync(It.IsAny<TenantMembershipRoleBootstrapRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task BootstrapDoesNotResurrectDisabledOrganization()
    {
        var id = Guid.NewGuid();
        var tenants = new Mock<IIdentityTenantService>();
        tenants.Setup(x => x.FindByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityTenant(id, "Support", Status: IdentityTenantStatuses.Disabled));
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var service = Create(new() { OrganizationId = id, BootstrapEnabled = true }, tenants.Object,
            users.Object, Mock.Of<ITenantRoleService>(), Mock.Of<ITenantMembershipStore>());
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.BootstrapAsync(new()
        { OrganizationName = "Support", AdministratorEmail = "support@example.test" }));
        users.VerifyNoOtherCalls();
    }

    private static ManagementIdentityBootstrapService Create(ManagementIdentityOptions options, IIdentityTenantService tenants,
        IIdentityUserStore users, ITenantRoleService roles, ITenantMembershipStore memberships)
        => new(Options.Create(options), tenants, users, roles, memberships, new ServiceOperationSystemContext(), new ServiceOperationExecutor());
}
