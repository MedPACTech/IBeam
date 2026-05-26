namespace IBeam.Services.Abstractions;

public interface IAuditActorProvider
{
    string? GetActorId();
}

public sealed class NoOpAuditActorProvider : IAuditActorProvider
{
    public string? GetActorId() => null;
}

