namespace IBeam.Identity.Interfaces;

using IBeam.Identity.Models;

public interface IIdentityRegistrationService
{
    Task<IdentityUser> RegisterAsync(RegisterUserRequest request, CancellationToken ct = default);
}
