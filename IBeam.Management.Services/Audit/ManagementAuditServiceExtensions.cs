using System.Runtime.CompilerServices;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services.Audit;
/// <summary>Uses a separate executor/sink for Management packages; preserves the host's existing executor and audit policy for all other services.</summary>
public sealed class ManagementAuditOperationExecutor(IServiceOperationExecutor original, ServiceOperationExecutor management) : IServiceOperationExecutor
{
    private IServiceOperationExecutor For(object instance) => ManagementAuditOperations.IsManagementService(instance) ? management : original;
    public Task<T> ExecuteAsync<T>(object serviceInstance, Func<CancellationToken, Task<T>> operation, ServiceOperationExecutionOptions? options = null, CancellationToken ct = default, [CallerMemberName] string? callerMemberName = null)
        => For(serviceInstance).ExecuteAsync(serviceInstance, operation, options, ct, callerMemberName);
    public Task ExecuteAsync(object serviceInstance, Func<CancellationToken, Task> operation, ServiceOperationExecutionOptions? options = null, CancellationToken ct = default, [CallerMemberName] string? callerMemberName = null)
        => For(serviceInstance).ExecuteAsync(serviceInstance, operation, options, ct, callerMemberName);
    public T Execute<T>(object serviceInstance, Func<T> operation, ServiceOperationExecutionOptions? options = null, [CallerMemberName] string? callerMemberName = null)
        => For(serviceInstance).Execute(serviceInstance, operation, options, callerMemberName);
    public void Execute(object serviceInstance, Action operation, ServiceOperationExecutionOptions? options = null, [CallerMemberName] string? callerMemberName = null)
        => For(serviceInstance).Execute(serviceInstance, operation, options, callerMemberName);
}
public static class ManagementAuditServiceExtensions
{
    public static IServiceCollection AddIBeamManagementAudit(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(ManagementAuditSink))) return services;
        services.AddIBeamManagementServices();
        services.AddOptions<ManagementAuditOptions>().Bind(configuration.GetSection(ManagementAuditOptions.SectionName))
            .Validate(x => x.IsValid(), "Bounded audit retention and timeout are required.").ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IManagementAuditContext, ManagementAuditContext>();
        services.AddScoped<ManagementAuditSink>();
        var previous = services.Last(x => x.ServiceType == typeof(IServiceOperationExecutor));
        services.Remove(previous);
        services.AddScoped<IServiceOperationExecutor>(provider =>
        {
            var original = previous.ImplementationInstance as IServiceOperationExecutor ??
                (previous.ImplementationFactory is not null ? (IServiceOperationExecutor)previous.ImplementationFactory(provider) :
                (IServiceOperationExecutor)ActivatorUtilities.CreateInstance(provider, previous.ImplementationType!));
            return new ManagementAuditOperationExecutor(original, new ServiceOperationExecutor(auditTrailSink: provider.GetRequiredService<ManagementAuditSink>(),
                auditOptionsMonitor: new AuditMonitor()));
        });
        return services;
    }
    private sealed class AuditMonitor : IOptionsMonitor<ServiceAuditOptions>
    {
        public ServiceAuditOptions CurrentValue { get; } = new() { Enabled = true, FailOnAuditError = true, CaptureBefore = false, CaptureAfter = false };
        public ServiceAuditOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ServiceAuditOptions, string?> listener) => null;
    }
}
