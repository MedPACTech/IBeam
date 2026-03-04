namespace IBeam.Identity.Interfaces;

public interface IAuthEventPublisher
{
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : class;
}

