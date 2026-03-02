namespace IBeam.Api.Infrastructure;

public sealed class ApiConfigurationBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    internal ApiConfigurationBuilder(IServiceCollection services, IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
    }

    public ApiConfigurationBuilder BindOptions<TOptions>(string? sectionName = null)
        where TOptions : class
    {
        var section = string.IsNullOrWhiteSpace(sectionName)
            ? typeof(TOptions).Name
            : sectionName;

        _services.Configure<TOptions>(_configuration.GetSection(section));
        return this;
    }
}
