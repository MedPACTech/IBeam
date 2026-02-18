namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface IIdentityRegistrationService
{
    Task<IdentityUser> RegisterAsync(RegisterUserRequest request, CancellationToken ct = default);
}
