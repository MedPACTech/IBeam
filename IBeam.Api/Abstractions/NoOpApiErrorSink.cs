namespace IBeam.Api.Abstractions;

public sealed class NoOpApiErrorSink : IApiErrorSink
{
    public Task SaveAsync(ApiErrorRecord error, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
