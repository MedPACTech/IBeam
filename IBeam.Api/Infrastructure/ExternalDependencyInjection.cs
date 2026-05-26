namespace IBeam.Api.Infrastructure;

public static class ExternalDependencyInjection
{
    public static IServiceCollection AddExternalClients(
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
