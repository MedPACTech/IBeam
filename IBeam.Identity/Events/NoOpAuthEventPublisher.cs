using IBeam.Identity.Interfaces;

namespace IBeam.Identity.Events;

public sealed class NoOpAuthEventPublisher : IAuthEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : class
        => Task.CompletedTask;
}

