using Azure.Data.Tables;
using ElCamino.AspNetCore.Identity.AzureTable;
using ElCamino.AspNetCore.Identity.AzureTable.Model;
using IBeam.Identity.Repositories.AzureTable.Options;
using Microsoft.AspNetCore.Identity;


var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register Azure Table options (replace with your actual config section)
builder.Services.AddOptions<AzureTableIdentityOptions>()
    .Configure(o =>
    {
        o.StorageConnectionString = "UseDevelopmentStorage=true"; // or your real connection string
        // Optionally set TablePrefix, IndexTableName, etc.
    });

// Register TableServiceClient and IdentityConfiguration as singletons
builder.Services.AddSingleton<TableServiceClient>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureTableIdentityOptions>>().Value;
    return new TableServiceClient(opts.StorageConnectionString);
});
builder.Services.AddSingleton<IdentityConfiguration>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureTableIdentityOptions>>().Value;
    return new IdentityConfiguration
    {
        TablePrefix = opts.TablePrefix,
        IndexTableName = opts.IndexTableName,
        UserTableName = opts.UserTableName,
        RoleTableName = opts.RoleTableName,
    };
});

// Register IdentityCloudContext as scoped
builder.Services.AddScoped<IdentityCloudContext>(sp =>
{
    var cfg = sp.GetRequiredService<IdentityConfiguration>();
    var client = sp.GetRequiredService<TableServiceClient>();
    return new IdentityCloudContext(cfg, client);
});

// Register ElCamino Identity
var identityBuilder = builder.Services
    .AddIdentityCore<ElCamino.AspNetCore.Identity.AzureTable.Model.IdentityUser>(options => { })
    .AddRoles<ElCamino.AspNetCore.Identity.AzureTable.Model.IdentityRole>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

identityBuilder.AddAzureTableStores<IdentityCloudContext>(
    sp => sp.GetRequiredService<IdentityConfiguration>(),
    sp => sp.GetRequiredService<TableServiceClient>());

builder.Services.AddDataProtection();
builder.Services.AddCors();


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.UseCors(policy =>
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader());
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var opts = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureTableIdentityOptions>>().Value;
    Console.WriteLine($"AzureTableIdentityOptions: StorageConnectionString={opts.StorageConnectionString}, UserTableName={opts.UserTableName}, RoleTableName={opts.RoleTableName}");

    if (string.IsNullOrWhiteSpace(opts.StorageConnectionString))
    {
        throw new InvalidOperationException("AzureTableIdentityOptions.StorageConnectionString is missing!");
    }
}

app.Run();
