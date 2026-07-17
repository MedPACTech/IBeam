using System.Runtime.CompilerServices;

namespace IBeam.Services.Abstractions;

public interface IServiceOperationExecutor
{
    Task ExecuteAsync(
        object serviceInstance,
        Func<CancellationToken, Task> operation,
        ServiceOperationExecutionOptions? options = null,
        CancellationToken ct = default,
        [CallerMemberName] string? callerMemberName = null);

    Task<TResult> ExecuteAsync<TResult>(
        object serviceInstance,
        Func<CancellationToken, Task<TResult>> operation,
        ServiceOperationExecutionOptions? options = null,
        CancellationToken ct = default,
        [CallerMemberName] string? callerMemberName = null);

    void Execute(
        object serviceInstance,
        Action operation,
        ServiceOperationExecutionOptions? options = null,
        [CallerMemberName] string? callerMemberName = null);

    TResult Execute<TResult>(
        object serviceInstance,
        Func<TResult> operation,
        ServiceOperationExecutionOptions? options = null,
        [CallerMemberName] string? callerMemberName = null);
}

public sealed class ServiceOperationExecutionOptions
{
    public string? OperationName { get; set; }

    public string? AuditAction { get; set; }

    public string? EntityName { get; set; }

    public ServiceAuditOperation AuditOperation { get; set; } = ServiceAuditOperation.Custom;

    public Guid? EntityId { get; set; }

    public bool? AuditEnabled { get; set; }

    public bool? PermissionEnabled { get; set; }

    public List<string> RequiredPermissionNames { get; set; } = [];

    public string? OriginalJson { get; set; }

    public string? TransformedJson { get; set; }

    public object? OriginalData { get; set; }

    public object? TransformedData { get; set; }
}
