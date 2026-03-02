namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface IIdentityUserStore
{
    Task<IdentityUser?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<IdentityUser?> FindByPhoneAsync(string phoneNumber, CancellationToken ct = default);
    Task<IdentityUser?> FindByIdAsync(Guid userId, CancellationToken ct = default);

    Task<CreateUserResult> CreateAsync(RegisterUserRequest request, CancellationToken ct = default);
    Task<bool> ValidatePasswordAsync(string emailOrPhone, string password, CancellationToken ct = default);
    Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default);
    Task SetEmailConfirmedAsync(Guid userId, bool confirmed, CancellationToken ct = default);
    Task SetTwoFactorAsync(Guid userId, bool enabled, string? preferredMethod = null, CancellationToken ct = default);

    Task UpdateEmailAsync(Guid userId, string newEmail, CancellationToken ct = default);
    Task UpdatePhoneAsync(Guid userId, string newPhone, CancellationToken ct = default);
}
