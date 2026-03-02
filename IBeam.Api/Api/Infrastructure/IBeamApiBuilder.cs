namespace IBeam.Api.Infrastructure;

public sealed class IBeamApiBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    internal IBeamApiBuilder(IServiceCollection services, IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
    }

    public IBeamApiBuilder ConfigureOptions(Action<ApiConfigurationBuilder> configure)
    {
        _services.AddIBeamApiConfigurations(_configuration, configure);
        return this;
    }

    public IBeamApiBuilder AddServices(params Action<IServiceCollection>[] registrations)
    {
        _services.AddManagedServices(registrations);
        return this;
    }

    public IBeamApiBuilder AddRepositories(params Action<IServiceCollection, IConfiguration>[] registrations)
    {
        _services.AddRepositories(_configuration, registrations);
        return this;
    }

    public IBeamApiBuilder AddExternalClients(params Action<IServiceCollection>[] registrations)
    {
        _services.AddExternalClients(registrations);
        return this;
    }
}
