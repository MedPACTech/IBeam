namespace IBeam.Identity.Options;

public sealed class AuthEventOptions
{
    public const string SectionName = "IBeam:Identity:Events";

    // false => log and continue on publish/hook failures
    // true  => fail auth flow on publish/hook failures
    public bool StrictPublishFailures { get; set; } = false;
}

