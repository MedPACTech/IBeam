using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Tenants;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class TenantExtensionTests
{
    [TestMethod]
    public async Task EnsureAsync_CreatesExtension_WhenMissing()
    {
        var tenantId = Guid.NewGuid();
        var identityTenant = new IdentityTenant(tenantId, "Hubbsly", "HUBBSLY");
        var created = new AppTenant(tenantId, "hubbsly");
        var store = new Mock<ITenantExtensionStore<AppTenant>>(MockBehavior.Strict);

        store.Setup(x => x.FindByTenantIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppTenant?)null);
        store.Setup(x => x.CreateAsync(
                identityTenant,
                It.Is<TenantExtensionContext>(c => c.Operation == TenantExtensionOperations.Created),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var resolver = new TenantExtensionResolver<AppTenant>(store.Object);

        var result = await resolver.EnsureAsync(
            identityTenant,
            TenantExtensionContext.Create(TenantExtensionOperations.Created));

        Assert.AreSame(created, result);
        store.VerifyAll();
    }

    [TestMethod]
    public async Task EnsureAsync_UpdatesExtension_WhenPresent()
    {
        var tenantId = Guid.NewGuid();
        var identityTenant = new IdentityTenant(tenantId, "Hubbsly Care", "HUBBSLY CARE");
        var existing = new AppTenant(tenantId, "hubbsly");
        var updated = existing with { Slug = "hubbsly-care" };
        var store = new Mock<ITenantExtensionStore<AppTenant>>(MockBehavior.Strict);

        store.Setup(x => x.FindByTenantIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        store.Setup(x => x.UpdateFromIdentityTenantAsync(
                existing,
                identityTenant,
                It.Is<TenantExtensionContext>(c => c.Operation == TenantExtensionOperations.Updated),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var resolver = new TenantExtensionResolver<AppTenant>(store.Object);

        var result = await resolver.EnsureAsync(
            identityTenant,
            TenantExtensionContext.Create(TenantExtensionOperations.Updated));

        Assert.AreSame(updated, result);
        store.VerifyAll();
    }

    [TestMethod]
    public async Task TenantInfoResolver_UsesMetadataProvider_ForDisplayNameAndActiveState()
    {
        var tenantId = Guid.NewGuid();
        var provider = new Mock<ITenantMetadataProvider>(MockBehavior.Strict);
        provider.Setup(x => x.GetTenantMetadataAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantMetadata(
                tenantId,
                DisplayName: "Hubbsly Practice",
                IsActive: false));

        var resolver = new TenantInfoResolver(provider.Object);

        var result = await resolver.EnrichAsync(
            new TenantInfo(tenantId, "Identity Name", Array.Empty<string>(), true));

        Assert.IsNotNull(result);
        Assert.AreEqual("Hubbsly Practice", result.Name);
        Assert.IsFalse(result.IsActive);
        provider.VerifyAll();
    }

    [TestMethod]
    public async Task TenantSelectionService_GetTenantsForUser_EnrichesAndEnsuresExtension()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var memberships = new[]
        {
            new TenantInfo(tenantId, "Identity Name", Array.Empty<string>(), true)
        };

        var membershipStore = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var infoResolver = new Mock<ITenantInfoResolver>(MockBehavior.Strict);
        var coordinator = new Mock<ITenantExtensionCoordinator>(MockBehavior.Strict);

        membershipStore.Setup(x => x.GetTenantsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(memberships);
        infoResolver.Setup(x => x.EnrichAsync(memberships, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TenantInfo(tenantId, "Hubbsly Practice", Array.Empty<string>(), true)
            });
        coordinator.Setup(x => x.EnsureExtensionAsync(
                It.Is<IdentityTenant>(t => t.TenantId == tenantId && t.Name == "Identity Name"),
                It.Is<TenantExtensionContext>(c =>
                    c.Operation == TenantExtensionOperations.Listed &&
                    c.AuthUserId == userId),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new TenantSelectionService(
            membershipStore.Object,
            Mock.Of<ITokenService>(),
            infoResolver.Object,
            coordinator.Object);

        var result = await service.GetTenantsForUserAsync(userId);

        Assert.AreEqual("Hubbsly Practice", result[0].Name);
        membershipStore.VerifyAll();
        infoResolver.VerifyAll();
        coordinator.VerifyAll();
    }

    public sealed record AppTenant(Guid TenantId, string Slug) : IIdentityTenantExtension;
}
