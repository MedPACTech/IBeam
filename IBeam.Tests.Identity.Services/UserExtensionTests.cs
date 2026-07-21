using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Users;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class UserExtensionTests
{
    [TestMethod]
    public async Task EnsureAsync_CreatesExtension_WhenMissing()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var identityUser = new IdentityUser(userId, "abram@example.com", true, DisplayName: "Abram");
        var created = new AppUser(userId, tenantId, "Abram");
        var store = new Mock<IIdentityUserExtensionStore<AppUser>>(MockBehavior.Strict);

        store.Setup(x => x.FindByUserIdAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppUser?)null);
        store.Setup(x => x.CreateAsync(
                identityUser,
                It.Is<UserExtensionContext>(c =>
                    c.Operation == UserExtensionOperations.Created &&
                    c.UserId == userId &&
                    c.TenantId == tenantId &&
                    c.NormalizedEmail == "abram@example.com" &&
                    c.DisplayName == "Abram"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var resolver = new IdentityUserExtensionResolver<AppUser>(store.Object);

        var result = await resolver.EnsureAsync(
            identityUser,
            UserExtensionContext.Create(
                UserExtensionOperations.Created,
                userId,
                tenantId,
                identityUser.Email,
                identityUser.PhoneNumber,
                identityUser.DisplayName));

        Assert.AreSame(created, result);
        store.VerifyAll();
    }

    [TestMethod]
    public async Task EnsureAsync_UpdatesExtension_WhenPresent()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var identityUser = new IdentityUser(userId, "new@example.com", true);
        var existing = new AppUser(userId, tenantId, "Old");
        var updated = new AppUser(userId, tenantId, "New");
        var store = new Mock<IIdentityUserExtensionStore<AppUser>>(MockBehavior.Strict);

        store.Setup(x => x.FindByUserIdAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        store.Setup(x => x.UpdateFromIdentityUserAsync(
                existing,
                identityUser,
                It.Is<UserExtensionContext>(c => c.Operation == UserExtensionOperations.Login),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var resolver = new IdentityUserExtensionResolver<AppUser>(store.Object);

        var result = await resolver.EnsureAsync(
            identityUser,
            UserExtensionContext.Create(UserExtensionOperations.Login, userId, tenantId, identityUser.Email));

        Assert.AreSame(updated, result);
        store.VerifyAll();
    }

    [TestMethod]
    public void ResolveDisplayName_UsesExplicitValueBeforeEmailOrPhone()
    {
        var result = IdentityUserDefaults.ResolveDisplayName(
            "  Abram  ",
            "abram@example.com",
            "+16145551212");

        Assert.AreEqual("Abram", result);
    }

    [TestMethod]
    public void ResolveDisplayName_FallsBackToEmailThenPhone()
    {
        var emailResult = IdentityUserDefaults.ResolveDisplayName(
            null,
            "  abram@example.com  ",
            "+16145551212");
        var phoneResult = IdentityUserDefaults.ResolveDisplayName(
            null,
            null,
            "  +16145551212  ");

        Assert.AreEqual("abram@example.com", emailResult);
        Assert.AreEqual("+16145551212", phoneResult);
    }

    [TestMethod]
    public void SyncIdentityContact_CopiesIdentityEmailAndPhoneWithoutContactFields()
    {
        var userId = Guid.NewGuid();
        var profile = new AppUser(userId, null, "Abram")
        {
            ContactEmail = "support@example.com",
            ContactPhoneNumber = "+16145550000"
        };
        var identityUser = new IdentityUser(
            userId,
            "  Abram@Example.com  ",
            true,
            PhoneNumber: "  +16145551212  ");

        IdentityUserDefaults.SyncIdentityContact(profile, identityUser);

        Assert.AreEqual("abram@example.com", profile.IdentityEmail);
        Assert.AreEqual("+16145551212", profile.IdentityPhoneNumber);
        Assert.AreEqual("support@example.com", profile.ContactEmail);
        Assert.AreEqual("+16145550000", profile.ContactPhoneNumber);
    }

    public sealed class AppUser : IIdentityUserProfileExtension, IIdentityUserContactProjection
    {
        public AppUser(Guid userId, Guid? tenantId, string displayName)
        {
            UserId = userId;
            TenantId = tenantId;
            DisplayName = displayName;
        }

        public Guid UserId { get; set; }
        public Guid? TenantId { get; set; }
        public string DisplayName { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? IdentityEmail { get; set; }
        public string? IdentityPhoneNumber { get; set; }
        public string? ContactEmail { get; set; }
        public string? ContactPhoneNumber { get; set; }
    }
}
