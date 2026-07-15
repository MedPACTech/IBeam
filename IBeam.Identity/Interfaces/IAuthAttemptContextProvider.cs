using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IAuthAttemptContextProvider
{
    AuthAttemptContext GetCurrent();
}
