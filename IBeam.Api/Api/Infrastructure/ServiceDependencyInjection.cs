namespace IBeam.Api.Infrastructure;

public static class ServiceDependencyInjection
{
    public static IServiceCollection AddManagedServices(
        this IServiceCollection services,
        params Action<IServiceCollection>[] registrations)
    {
        foreach (var registration in registrations)
        {
            registration(services);
        }

        return services;
    }
}
