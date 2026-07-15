using System.Security.Claims;

namespace IBeam.Services.Abstractions;

public interface IServiceOperationPrincipalProvider
{
    ClaimsPrincipal? GetPrincipal();
}

public sealed class NoOpServiceOperationPrincipalProvider : IServiceOperationPrincipalProvider
{
    public ClaimsPrincipal? GetPrincipal() => null;
}

