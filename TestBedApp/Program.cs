using Azure.Data.Tables;
using ElCamino.AspNetCore.Identity.AzureTable;
using ElCamino.AspNetCore.Identity.AzureTable.Model;
using IBeam.Identity.Repositories.AzureTable.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using ElCaminoIdentityRole = ElCamino.AspNetCore.Identity.AzureTable.Model.IdentityRole;
using ElCaminoIdentityUser = ElCamino.AspNetCore.Identity.AzureTable.Model.IdentityUser;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOptions<AzureTableIdentityOptions>()
    .Bind(builder.Configuration.GetSection(AzureTableIdentityOptions.SectionName))
    .Validate(o =>
    {
        o.Validate();
        return true;
    })
    .ValidateOnStart();

var identityOptions = builder.Configuration
    .GetSection(AzureTableIdentityOptions.SectionName)
    .Get<AzureTableIdentityOptions>() ?? new AzureTableIdentityOptions();
identityOptions.Validate();

var identityTableClient = new TableServiceClient(identityOptions.StorageConnectionString);
var identityConfiguration = new IdentityConfiguration
{
    TablePrefix = identityOptions.TablePrefix,
    IndexTableName = identityOptions.IndexTableName,
    UserTableName = identityOptions.UserTableName,
    RoleTableName = identityOptions.RoleTableName,
};

builder.Services.AddSingleton(identityTableClient);
builder.Services.AddSingleton(identityConfiguration);

builder.Services.AddScoped<IdentityCloudContext>(sp =>
{
    var cfg = sp.GetRequiredService<IdentityConfiguration>();
    var client = sp.GetRequiredService<TableServiceClient>();
    return new IdentityCloudContext(cfg, client);
});

builder.Services
    .AddIdentity<ElCaminoIdentityUser, ElCaminoIdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddAzureTableStores<IdentityCloudContext>(
        _ => identityConfiguration,
        _ => identityTableClient);

builder.Services.AddAuthorization();

var app = builder.Build();

var runStartupDiags = builder.Configuration.GetValue<bool>("IBeam:Identity:RunStartupDiagnostics");
if (runStartupDiags)
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    await LogResolveResult<IUserStore<ElCaminoIdentityUser>>(sp, "IUserStore<IdentityUser>");
    await LogResolveResult<UserManager<ElCaminoIdentityUser>>(sp, "UserManager<IdentityUser>");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

await identityTableClient.CreateTableIfNotExistsAsync(identityConfiguration.TablePrefix + identityConfiguration.IndexTableName);
await identityTableClient.CreateTableIfNotExistsAsync(identityConfiguration.TablePrefix + identityConfiguration.UserTableName);
await identityTableClient.CreateTableIfNotExistsAsync(identityConfiguration.TablePrefix + identityConfiguration.RoleTableName);

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static async Task LogResolveResult<T>(IServiceProvider services, string name) where T : class
{
    var sw = Stopwatch.StartNew();
    var resolveTask = Task.Run(() => services.GetService<T>());
    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(8));
    var completed = await Task.WhenAny(resolveTask, timeoutTask);
    sw.Stop();

    if (completed == timeoutTask)
    {
        Console.WriteLine($"[Startup DI] {name}: TIMEOUT after {sw.ElapsedMilliseconds}ms");
        return;
    }

    var instance = await resolveTask;
    var status = instance is null ? "NULL" : "OK";
    Console.WriteLine($"[Startup DI] {name}: {status} in {sw.ElapsedMilliseconds}ms");
}
