namespace IBeam.Api.Abstractions;

public interface IApiErrorSink
{
    Task SaveAsync(ApiErrorRecord error, CancellationToken cancellationToken = default);
}
