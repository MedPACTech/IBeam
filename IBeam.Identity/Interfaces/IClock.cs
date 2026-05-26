namespace IBeam.Identity.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
