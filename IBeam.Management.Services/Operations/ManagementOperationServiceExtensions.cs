using IBeam.Management.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IBeam.Management.Services;

/// <summary>Trusted code registration only. Do not bind descriptors or CLR types from configuration or HTTP input.</summary>
public static class ManagementOperationServiceExtensions
{
    private sealed record Registration(ManagementOperationDescriptor Descriptor);
    public static IServiceCollection AddIBeamManagementOperationRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton(provider => new ManagementOperationRegistry(provider.GetServices<Registration>().Select(x => x.Descriptor)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ValidateRegistry>());
        return services;
    }

    /// <summary>Installs the typed handler and descriptor together. Existing grants and enabled-operation lists are never changed.</summary>
    public static IServiceCollection AddIBeamManagementOperation<TRequest, TResult, THandler>(this IServiceCollection services,
        ManagementOperationDescriptor descriptor) where THandler : class, IManagementOperationHandler<TRequest, TResult>
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.HandlerServiceType != typeof(IManagementOperationHandler<TRequest, TResult>))
            throw new ArgumentException("The descriptor must bind the registered typed handler.", nameof(descriptor));
        services.AddIBeamManagementOperationRegistry();
        services.AddSingleton(new Registration(descriptor with { RequiredGrants = Array.AsReadOnly(descriptor.RequiredGrants.ToArray()) }));
        services.TryAddScoped<IManagementOperationHandler<TRequest, TResult>, THandler>();
        return services;
    }

    private sealed class ValidateRegistry(ManagementOperationRegistry registry) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) { _ = registry.Operations.Count; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
