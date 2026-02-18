namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface IIdentityUserStore
{
    Task<IdentityUser?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<IdentityUser?> FindByIdAsync(Guid userId, CancellationToken ct = default);

    Task<IdentityUser> CreateAsync(RegisterUserRequest request, CancellationToken ct = default);
    Task<bool> ValidatePasswordAsync(string email, string password, CancellationToken ct = default);
}
