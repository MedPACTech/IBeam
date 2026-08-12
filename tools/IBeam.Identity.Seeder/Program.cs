using System.Text.Json;
using IBeam.AccessControl;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Repositories.AzureTable.Extensions;
using IBeam.Identity.Schema;
using IBeam.Identity.Seeder;
using IBeam.Identity.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

var options = SeederCliOptions.Parse(args);
if (options.ShowHelp)
{
    SeederCliOptions.WriteHelp(Console.Out);
    return 0;
}

if (!options.Apply)
{
    Console.WriteLine("Dry-run mode. Pass --apply, or use scripts/identity/seed-identity.ps1 -Apply, to write changes.");
}

var contentRoot = Path.GetFullPath(options.ContentRoot ?? Directory.GetCurrentDirectory());
var configPath = Path.GetFullPath(Path.IsPathRooted(options.ConfigPath)
    ? options.ConfigPath
    : Path.Combine(contentRoot, options.ConfigPath));

var seedConfig = await SeedIdentityConfig.LoadAsync(configPath);
seedConfig.Validate();

var configuration = new ConfigurationBuilder()
    .SetBasePath(contentRoot)
    .AddJsonFile(Path.Combine("IBeam.Identity.Api", "appsettings.json"), optional: true, reloadOnChange: false)
    .AddJsonFile(Path.Combine("IBeam.Identity.Api", $"appsettings.{options.Environment}.json"), optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging(builder =>
{
    builder.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    });
    builder.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Warning);
});

services.AddIBeamIdentityServices(configuration);
services.AddIBeamIdentityAzureTable(configuration);
services.TryAddScoped<IIdentityCommunicationSender, LocalSeedIdentityCommunicationSender>();

await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateScopes = true
});

using var scope = provider.CreateScope();
if (options.EnsureSchema)
{
    var schema = scope.ServiceProvider.GetService<IIdentitySchemaManager>();
    if (schema is not null)
    {
        var status = await schema.GetStatusAsync();
        if (!status.IsUpToDate)
        {
            if (options.Apply)
            {
                await schema.ApplyAsync();
            }

            Console.WriteLine(options.Apply
                ? $"Applied identity schema to version {status.TargetVersion}."
                : $"Would apply identity schema from version {status.CurrentVersion} to {status.TargetVersion}.");
        }
    }
}

var runner = new IdentitySeedRunner(
    scope.ServiceProvider.GetRequiredService<IIdentityTenantService>(),
    scope.ServiceProvider.GetRequiredService<ITenantRoleService>(),
    scope.ServiceProvider.GetRequiredService<IIdentityUserStore>(),
    scope.ServiceProvider.GetRequiredService<IPermissionRoleMapService>(),
    scope.ServiceProvider.GetRequiredService<IResourceAccessService>());

var result = await runner.RunAsync(seedConfig, new IdentitySeedRunOptions(options.Apply));

var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true
};

var reportJson = JsonSerializer.Serialize(result, serializerOptions);
Console.WriteLine(reportJson);

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    var reportPath = Path.GetFullPath(Path.IsPathRooted(options.ReportPath)
        ? options.ReportPath
        : Path.Combine(contentRoot, options.ReportPath));
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    await File.WriteAllTextAsync(reportPath, reportJson);
    Console.WriteLine($"Wrote report to {reportPath}");
}

return result.Failures.Count == 0 ? 0 : 1;
