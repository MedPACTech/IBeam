using IBeam.Api.Infrastructure;
using IBeam.Api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Api;

[TestClass]
public sealed class ApiInfrastructureTests
{
    [TestMethod]
    public void AddIBeamApi_BindsApiErrorHandlingOptions_AndRegistersCoreServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiErrorHandling:ExposeDetailedErrors"] = "true",
                ["ApiErrorHandling:IncludeTraceIdInResponse"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddIBeamApi(config);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ApiErrorHandlingOptions>>().Value;

        Assert.IsTrue(options.ExposeDetailedErrors);
        Assert.IsFalse(options.IncludeTraceIdInResponse);

        var hosted = provider.GetServices<IHostedService>();
        Assert.IsTrue(hosted.Any(x => x.GetType().Name == "GlobalErrorHandler"));
        Assert.IsNotNull(provider.GetService<IHttpContextAccessor>());
    }

    [TestMethod]
    public void AddIBeamApi_WhenConfigureProvided_InvokesBuilderRegistrations()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Custom:Value"] = "hello"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddIBeamApi(config, api =>
        {
            api.ConfigureOptions(cfg => cfg.BindOptions<CustomOptions>("Custom"));
            api.AddServices(s => s.AddSingleton<MarkerService>());
            api.AddRepositories((s, c) => s.AddSingleton(new RepositoryMarker(c["Custom:Value"] ?? string.Empty)));
            api.AddExternalClients(s => s.AddSingleton<ExternalClientMarker>());
        });

        using var provider = services.BuildServiceProvider();

        Assert.IsNotNull(provider.GetService<MarkerService>());
        Assert.IsNotNull(provider.GetService<ExternalClientMarker>());

        var repoMarker = provider.GetRequiredService<RepositoryMarker>();
        Assert.AreEqual("hello", repoMarker.Value);

        var custom = provider.GetRequiredService<IOptions<CustomOptions>>().Value;
        Assert.AreEqual("hello", custom.Value);
    }

    [TestMethod]
    public void AddIBeamApiConfigurations_BindsNamedOptionsSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MySection:Value"] = "bound"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddIBeamApiConfigurations(config, b => b.BindOptions<CustomOptions>("MySection"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CustomOptions>>().Value;

        Assert.AreEqual("bound", options.Value);
    }

    [TestMethod]
    public void UseIBeamApi_AddsMiddlewareToPipeline()
    {
        var builder = new TestApplicationBuilder();

        var returned = builder.UseIBeamApi();

        Assert.AreSame(builder, returned);
        Assert.AreEqual(1, builder.UseCallCount);
    }

    private sealed class CustomOptions
    {
        public string? Value { get; set; }
    }

    private sealed class MarkerService;
    private sealed class ExternalClientMarker;
    private sealed record RepositoryMarker(string Value);

    private sealed class TestApplicationBuilder : IApplicationBuilder
    {
        private readonly IDictionary<string, object?> _properties = new Dictionary<string, object?>();

        public IServiceProvider ApplicationServices { get; set; } = new ServiceCollection().BuildServiceProvider();
        public IFeatureCollection ServerFeatures { get; } = new FeatureCollection();
        public IDictionary<string, object?> Properties => _properties;
        public int UseCallCount { get; private set; }

        public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware)
        {
            UseCallCount++;
            return this;
        }

        public IApplicationBuilder New() => new TestApplicationBuilder();

        public RequestDelegate Build() => _ => Task.CompletedTask;
    }
}
