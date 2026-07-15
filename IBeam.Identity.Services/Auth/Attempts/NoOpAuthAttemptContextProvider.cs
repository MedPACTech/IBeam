using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Auth.Attempts;

public sealed class NoOpAuthAttemptContextProvider : IAuthAttemptContextProvider
{
    public AuthAttemptContext GetCurrent() => new();
}
