namespace IBeam.Api.Infrastructure;

public static class RepositoryDependencyInjection
{
    public static IServiceCollection AddRepositories(
        this IServiceCollection services,
        IConfiguration configuration,
        params Action<IServiceCollection, IConfiguration>[] registrations)
    {
        foreach (var registration in registrations)
        {
            registration(services, configuration);
        }

        return services;
    }
}
