using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Identity.Target.Api;

public static class ManagementCustomerUserDirectoryApiExtensions
{
    public static IServiceCollection AddIBeamManagementCustomerUserDirectoryApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementCustomerUserDirectory(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IManagementCustomerIdentityCapability, UserCapability>());
        services.AddHostedService<UserValidator>();
        services.AddControllers().ConfigureApplicationPartManager(x =>
        {
            if (!x.FeatureProviders.OfType<Feature<ManagementCustomerUserDirectoryController>>().Any()) x.FeatureProviders.Add(new Feature<ManagementCustomerUserDirectoryController>());
        });
        return services;
    }
    public static IServiceCollection AddIBeamManagementCustomerInvitationDirectoryApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementCustomerInvitationDirectory(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IManagementCustomerIdentityCapability, InviteCapability>());
        services.AddHostedService<InviteValidator>();
        services.AddControllers().ConfigureApplicationPartManager(x =>
        {
            if (!x.FeatureProviders.OfType<Feature<ManagementCustomerInvitationDirectoryController>>().Any()) x.FeatureProviders.Add(new Feature<ManagementCustomerInvitationDirectoryController>());
        });
        return services;
    }
    private sealed class UserCapability : IManagementCustomerIdentityCapability
    {
        public string Operation => ManagementCustomerIdentityOperations.UsersRead;
        public Type RequiredServiceType => typeof(IManagementCustomerUserDirectoryService);
    }
    private sealed class InviteCapability : IManagementCustomerIdentityCapability
    {
        public string Operation => ManagementCustomerIdentityOperations.InvitesRead;
        public Type RequiredServiceType => typeof(IManagementCustomerInvitationDirectoryService);
    }
    private sealed class Feature<T> : IApplicationFeatureProvider<ControllerFeature> where T : ControllerBase
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var type = typeof(T).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type);
        }
    }
    private sealed class UserValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementCustomerUserDirectoryService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class InviteValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementCustomerInvitationDirectoryService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
