using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Tenants;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class TenantRoleServiceTests
{
    [TestMethod]
    public async Task CreateRoleAsync_NormalizesName_AndCallsStore()
    {
        var tenantId = Guid.NewGuid();
        var store = new Mock<ITenantRoleStore>(MockBehavior.Strict);
        store.Setup(x => x.CreateRoleAsync(tenantId, "Provider Admin", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantRole(tenantId, Guid.NewGuid(), "Provider Admin", false, true, DateTimeOffset.UtcNow));

        var sut = new TenantRoleService(store.Object);
        var result = await sut.CreateRoleAsync(tenantId, "   Provider Admin   ");

        Assert.AreEqual("Provider Admin", result.Name);
        store.VerifyAll();
    }

    [TestMethod]
    public async Task CreateRoleAsync_EmptyName_ThrowsValidation()
    {
        var sut = new TenantRoleService(Mock.Of<ITenantRoleStore>());

        await Assert.ThrowsExactlyAsync<IdentityValidationException>(() =>
            sut.CreateRoleAsync(Guid.NewGuid(), "   "));
    }

    [TestMethod]
    public async Task GrantRolesAsync_EmptyRoleIds_ThrowsValidation()
    {
        var sut = new TenantRoleService(Mock.Of<ITenantRoleStore>());

        await Assert.ThrowsExactlyAsync<IdentityValidationException>(() =>
            sut.GrantRolesAsync(Guid.NewGuid(), Guid.NewGuid(), Array.Empty<Guid>()));
    }

    [TestMethod]
    public async Task UpdateRoleAsync_EmptyRoleId_ThrowsValidation()
    {
        var sut = new TenantRoleService(Mock.Of<ITenantRoleStore>());

        await Assert.ThrowsExactlyAsync<IdentityValidationException>(() =>
            sut.UpdateRoleAsync(Guid.NewGuid(), Guid.Empty, "Analyst"));
    }
}
