namespace IBeam.Api.Abstractions;

public interface ISystemLogSink
{
    Task SaveAsync(SystemLogRecord log, CancellationToken cancellationToken = default);
}

public sealed class NoOpSystemLogSink : ISystemLogSink
{
    public Task SaveAsync(SystemLogRecord log, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
