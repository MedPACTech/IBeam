using System.Runtime.CompilerServices;
using IBeam.Services.Abstractions;

namespace IBeam.Tests.Identity.Services;

internal sealed class RecordingServiceOperationExecutor : IServiceOperationExecutor
{
    private readonly List<ServiceOperationCall> _calls = [];

    public IReadOnlyList<ServiceOperationCall> Calls => _calls;

    public async Task ExecuteAsync(
        object serviceInstance,
        Func<CancellationToken, Task> operation,
        ServiceOperationExecutionOptions? options = null,
        CancellationToken ct = default,
        [CallerMemberName] string? callerMemberName = null)
    {
        _calls.Add(new ServiceOperationCall(serviceInstance.GetType(), callerMemberName, options));
        await operation(ct).ConfigureAwait(false);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        object serviceInstance,
        Func<CancellationToken, Task<TResult>> operation,
        ServiceOperationExecutionOptions? options = null,
        CancellationToken ct = default,
        [CallerMemberName] string? callerMemberName = null)
    {
        _calls.Add(new ServiceOperationCall(serviceInstance.GetType(), callerMemberName, options));
        return await operation(ct).ConfigureAwait(false);
    }

    public void Execute(
        object serviceInstance,
        Action operation,
        ServiceOperationExecutionOptions? options = null,
        [CallerMemberName] string? callerMemberName = null)
    {
        _calls.Add(new ServiceOperationCall(serviceInstance.GetType(), callerMemberName, options));
        operation();
    }

    public TResult Execute<TResult>(
        object serviceInstance,
        Func<TResult> operation,
        ServiceOperationExecutionOptions? options = null,
        [CallerMemberName] string? callerMemberName = null)
    {
        _calls.Add(new ServiceOperationCall(serviceInstance.GetType(), callerMemberName, options));
        return operation();
    }
}

internal sealed record ServiceOperationCall(
    Type ServiceType,
    string? CallerMemberName,
    ServiceOperationExecutionOptions? Options);
