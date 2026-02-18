namespace IBeam.Identity.Abstractions.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
