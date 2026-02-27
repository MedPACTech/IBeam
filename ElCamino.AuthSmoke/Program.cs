using Azure.Data.Tables;
using ElCamino.AspNetCore.Identity.AzureTable;
using ElCamino.AspNetCore.Identity.AzureTable.Model;
using Microsoft.AspNetCore.Identity;
using System.Diagnostics;
using ElCaminoIdentityRole = ElCamino.AspNetCore.Identity.AzureTable.Model.IdentityRole;
using ElCaminoIdentityUser = ElCamino.AspNetCore.Identity.AzureTable.Model.IdentityUser;

var builder = WebApplication.CreateBuilder(args);

var storageConnection = builder.Configuration["SmokeAuth:StorageConnectionString"] ?? "UseDevelopmentStorage=true";
var tablePrefix = builder.Configuration["SmokeAuth:TablePrefix"] ?? "Smoke";
var userStoreServiceType = typeof(IUserStore<ElCaminoIdentityUser>);
var userStoreDescriptor = builder.Services.LastOrDefault(d => d.ServiceType == userStoreServiceType);
var userStoreImplType = userStoreDescriptor?.ImplementationType;
var identityDescriptorSnapshot = builder.Services
    .Where(d =>
        (d.ServiceType.FullName?.Contains("Microsoft.AspNetCore.Identity") ?? false) ||
        (d.ImplementationType?.FullName?.Contains("UserStore") ?? false) ||
        (d.ImplementationType?.FullName?.Contains("RoleStore") ?? false))
    .Select(d => new
    {
        serviceType = d.ServiceType.FullName,
        implementationType = d.ImplementationType?.FullName,
        hasFactory = d.ImplementationFactory is not null,
        lifetime = d.Lifetime.ToString()
    })
    .ToList();

var identityTableClient = new TableServiceClient(storageConnection);
var identityConfig = new IdentityConfiguration
{
    TablePrefix = tablePrefix,
    IndexTableName = "AspNetIndex",
    UserTableName = "AspNetUsers",
    RoleTableName = "AspNetRoles",
};

builder.Services.AddSingleton(identityTableClient);
builder.Services.AddSingleton(identityConfig);

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
        _ => identityConfig,
        _ => identityTableClient);

builder.Services.AddAuthorization();

var app = builder.Build();

await LogStartupResolve<TableServiceClient>(app.Services, "TableServiceClient");
await LogStartupResolve<IdentityConfiguration>(app.Services, "IdentityConfiguration");
await LogStartupContextCtor(app.Services);

app.MapGet("/api/auth/ping", () => Results.Ok(new { ok = true, message = "Auth smoke app alive" }));

app.MapGet("/api/auth/storage-ping", async (TableServiceClient tableClient, CancellationToken ct) =>
{
    var table = tableClient.GetTableClient("smokehealth");
    await table.CreateIfNotExistsAsync(ct);
    return Results.Ok(new { ok = true, table = "smokehealth" });
});

app.MapGet("/api/auth/diag-iuserstore", async (HttpContext httpContext) =>
{
    var steps = new List<object>();
    var descriptorInfo = new
    {
        serviceType = userStoreServiceType.FullName,
        implementationType = userStoreDescriptor?.ImplementationType?.FullName,
        hasFactory = userStoreDescriptor?.ImplementationFactory is not null,
        lifetime = userStoreDescriptor?.Lifetime.ToString()
    };

    if (userStoreImplType is null)
    {
        return Results.Json(new { ok = false, descriptor = descriptorInfo, message = "No IUserStore implementation type found.", steps });
    }

    var ctors = userStoreImplType.GetConstructors()
        .OrderByDescending(c => c.GetParameters().Length)
        .ToList();

    var selectedCtor = ctors.FirstOrDefault();
    if (selectedCtor is null)
    {
        return Results.Json(new { ok = false, descriptor = descriptorInfo, message = "Implementation has no public constructor.", steps });
    }

    foreach (var p in selectedCtor.GetParameters())
    {
        var dep = await ResolveTypeWithTimeout(httpContext.RequestServices, p.ParameterType, "ctor:" + p.Name);
        steps.Add(new
        {
            step = "Resolve " + p.ParameterType.FullName,
            ok = dep.ok,
            elapsedMs = dep.elapsedMs,
            detail = dep.detail
        });
    }

    var instance = await CreateInstanceWithTimeout(httpContext.RequestServices, userStoreImplType);
    steps.Add(new
    {
        step = "Create " + userStoreImplType.FullName,
        ok = instance.ok,
        elapsedMs = instance.elapsedMs,
        detail = instance.detail
    });

    return Results.Json(new
    {
        ok = true,
        descriptor = descriptorInfo,
        constructor = selectedCtor.ToString(),
        steps
    });
});

app.MapGet("/api/auth/diag-identity-services", async (HttpContext httpContext) =>
{
    var checks = new List<object>();
    var sp = httpContext.RequestServices;

    var serviceTypes = new[]
    {
        typeof(IUserStore<ElCaminoIdentityUser>),
        typeof(IUserPasswordStore<ElCaminoIdentityUser>),
        typeof(IUserEmailStore<ElCaminoIdentityUser>),
        typeof(IUserRoleStore<ElCaminoIdentityUser>),
        typeof(IRoleStore<ElCaminoIdentityRole>),
        typeof(UserManager<ElCaminoIdentityUser>),
        typeof(SignInManager<ElCaminoIdentityUser>),
        typeof(IdentityCloudContext)
    };

    foreach (var type in serviceTypes)
    {
        var r = await ResolveTypeWithTimeout(sp, type, type.Name);
        checks.Add(new { service = type.FullName, ok = r.ok, elapsedMs = r.elapsedMs, detail = r.detail });
    }

    return Results.Json(new
    {
        ok = true,
        descriptor = new
        {
            serviceType = userStoreServiceType.FullName,
            implementationType = userStoreDescriptor?.ImplementationType?.FullName,
            hasFactory = userStoreDescriptor?.ImplementationFactory is not null,
            lifetime = userStoreDescriptor?.Lifetime.ToString()
        },
        identityDescriptors = identityDescriptorSnapshot,
        checks
    });
});

app.MapGet("/api/auth/diag-context-constructor", async (HttpContext httpContext) =>
{
    var sp = httpContext.RequestServices;
    var steps = new List<object>();

    var cfgRes = await ResolveTypeWithTimeout(sp, typeof(IdentityConfiguration), "IdentityConfiguration");
    steps.Add(new { step = "Resolve IdentityConfiguration", ok = cfgRes.ok, elapsedMs = cfgRes.elapsedMs, detail = cfgRes.detail });

    var clientRes = await ResolveTypeWithTimeout(sp, typeof(TableServiceClient), "TableServiceClient");
    steps.Add(new { step = "Resolve TableServiceClient", ok = clientRes.ok, elapsedMs = clientRes.elapsedMs, detail = clientRes.detail });

    if (!cfgRes.ok || !clientRes.ok)
    {
        return Results.Json(new { ok = false, message = "Could not resolve prerequisites.", steps });
    }

    var cfg = (IdentityConfiguration?)sp.GetService(typeof(IdentityConfiguration));
    var client = (TableServiceClient?)sp.GetService(typeof(TableServiceClient));
    if (cfg is null || client is null)
    {
        return Results.Json(new { ok = false, message = "Prerequisites were null after resolve.", steps });
    }

    var ctorResult = await CreateContextWithTimeout(cfg, client);
    steps.Add(new
    {
        step = "new IdentityCloudContext(cfg, client)",
        ok = ctorResult.ok,
        elapsedMs = ctorResult.elapsedMs,
        detail = ctorResult.detail
    });

    return Results.Json(new { ok = ctorResult.ok, steps });
});

app.MapPost("/api/auth/diag-register", async (RegisterRequest req, HttpContext httpContext) =>
{
    var steps = new List<object>();
    var resolveStore = await ResolveServiceWithTimeout<IUserStore<ElCaminoIdentityUser>>(httpContext.RequestServices, "Resolve IUserStore", steps);
    if (!resolveStore.ok || resolveStore.value is null)
    {
        return Results.Json(new { ok = false, message = "Could not resolve IUserStore.", steps }, statusCode: StatusCodes.Status504GatewayTimeout);
    }

    var store = resolveStore.value;
    if (store is not IUserPasswordStore<ElCaminoIdentityUser> passwordStore ||
        store is not IUserEmailStore<ElCaminoIdentityUser> emailStore)
    {
        steps.Add(new { step = "Validate store capabilities", ok = false, elapsedMs = 0L, detail = "Missing password/email interfaces." });
        return Results.Json(new { ok = false, message = "Store missing required interfaces.", steps }, statusCode: StatusCodes.Status500InternalServerError);
    }

    var normalizer = new UpperInvariantLookupNormalizer();
    var normalizedEmail = normalizer.NormalizeEmail(req.Email);

    var existingResult = await RunWithTimeout("FindByEmailAsync", ct => emailStore.FindByEmailAsync(normalizedEmail, ct), steps);
    if (!existingResult.ok)
    {
        return Results.Json(new { ok = false, message = "FindByEmailAsync timed out or failed.", steps }, statusCode: StatusCodes.Status504GatewayTimeout);
    }

    if (existingResult.value is not null)
    {
        return Results.Json(new { ok = false, message = "User already exists.", steps }, statusCode: StatusCodes.Status409Conflict);
    }

    var user = new ElCaminoIdentityUser
    {
        UserName = req.Email,
        Email = req.Email,
        NormalizedUserName = normalizer.NormalizeName(req.Email),
        NormalizedEmail = normalizedEmail,
        SecurityStamp = Guid.NewGuid().ToString("N")
    };

    var hasher = new PasswordHasher<ElCaminoIdentityUser>();
    var hash = hasher.HashPassword(user, req.Password);

    var setPwdResult = await RunWithTimeout<object?>(
        "SetPasswordHashAsync",
        async ct =>
        {
            await passwordStore.SetPasswordHashAsync(user, hash, ct);
            return null;
        },
        steps);

    if (!setPwdResult.ok)
    {
        return Results.Json(new { ok = false, message = "SetPasswordHashAsync timed out or failed.", steps }, statusCode: StatusCodes.Status504GatewayTimeout);
    }

    var createResult = await RunWithTimeout("CreateAsync", ct => store.CreateAsync(user, ct), steps);
    if (!createResult.ok)
    {
        return Results.Json(new { ok = false, message = "CreateAsync timed out or failed.", steps }, statusCode: StatusCodes.Status504GatewayTimeout);
    }

    if (!createResult.value!.Succeeded)
    {
        return Results.BadRequest(new { ok = false, errors = createResult.value.Errors.Select(e => new { e.Code, e.Description }), steps });
    }

    return Results.Ok(new { ok = true, userId = user.Id, email = user.Email, steps });
});

app.Run();

static async Task<(bool ok, T? value)> ResolveServiceWithTimeout<T>(IServiceProvider services, string stepName, List<object> steps) where T : class
{
    var sw = Stopwatch.StartNew();
    var resolveTask = Task.Run(() => services.GetService<T>());
    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(8));
    var completed = await Task.WhenAny(resolveTask, timeoutTask);
    sw.Stop();

    if (completed == timeoutTask)
    {
        steps.Add(new { step = stepName, ok = false, elapsedMs = sw.ElapsedMilliseconds, detail = "timeout" });
        return (false, null);
    }

    var value = await resolveTask;
    var ok = value is not null;
    steps.Add(new { step = stepName, ok, elapsedMs = sw.ElapsedMilliseconds, detail = ok ? "ok" : "null" });
    return (ok, value);
}

static async Task<(bool ok, long elapsedMs, string detail)> ResolveTypeWithTimeout(IServiceProvider services, Type serviceType, string stepName)
{
    var sw = Stopwatch.StartNew();
    var resolveTask = Task.Run(() => services.GetService(serviceType));
    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(8));
    var completed = await Task.WhenAny(resolveTask, timeoutTask);
    sw.Stop();

    if (completed == timeoutTask)
    {
        return (false, sw.ElapsedMilliseconds, stepName + ": timeout");
    }

    var value = await resolveTask;
    return value is null
        ? (false, sw.ElapsedMilliseconds, stepName + ": null")
        : (true, sw.ElapsedMilliseconds, stepName + ": ok");
}

static async Task<(bool ok, long elapsedMs, string detail)> CreateInstanceWithTimeout(IServiceProvider services, Type type)
{
    var sw = Stopwatch.StartNew();
    var createTask = Task.Run(() => ActivatorUtilities.CreateInstance(services, type));
    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(8));
    var completed = await Task.WhenAny(createTask, timeoutTask);
    sw.Stop();

    if (completed == timeoutTask)
    {
        return (false, sw.ElapsedMilliseconds, "timeout");
    }

    try
    {
        var instance = await createTask;
        return instance is null
            ? (false, sw.ElapsedMilliseconds, "null")
            : (true, sw.ElapsedMilliseconds, "ok");
    }
    catch (Exception ex)
    {
        return (false, sw.ElapsedMilliseconds, ex.GetType().Name + ": " + ex.Message);
    }
}

static async Task<(bool ok, long elapsedMs, string detail)> CreateContextWithTimeout(IdentityConfiguration cfg, TableServiceClient client)
{
    var sw = Stopwatch.StartNew();
    var task = Task.Run(() => new IdentityCloudContext(cfg, client));
    var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(8)));
    sw.Stop();

    if (completed != task)
    {
        return (false, sw.ElapsedMilliseconds, "timeout");
    }

    try
    {
        var ctx = await task;
        return ctx is null
            ? (false, sw.ElapsedMilliseconds, "null")
            : (true, sw.ElapsedMilliseconds, "ok");
    }
    catch (Exception ex)
    {
        return (false, sw.ElapsedMilliseconds, ex.GetType().Name + ": " + ex.Message);
    }
}

static async Task LogStartupResolve<T>(IServiceProvider services, string label) where T : class
{
    var r = await ResolveTypeWithTimeout(services, typeof(T), label);
    Console.WriteLine($"[Smoke Startup] {label}: {(r.ok ? "OK" : "FAIL")} {r.elapsedMs}ms ({r.detail})");
}

static async Task LogStartupContextCtor(IServiceProvider services)
{
    var cfg = services.GetService<IdentityConfiguration>();
    var client = services.GetService<TableServiceClient>();
    if (cfg is null || client is null)
    {
        Console.WriteLine("[Smoke Startup] IdentityCloudContext ctor: skipped (missing cfg/client)");
        return;
    }

    var r = await CreateContextWithTimeout(cfg, client);
    Console.WriteLine($"[Smoke Startup] IdentityCloudContext ctor: {(r.ok ? "OK" : "FAIL")} {r.elapsedMs}ms ({r.detail})");
}

static async Task<(bool ok, T? value)> RunWithTimeout<T>(string stepName, Func<CancellationToken, Task<T>> action, List<object> steps)
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var sw = Stopwatch.StartNew();

    try
    {
        var task = action(cts.Token);
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(8), CancellationToken.None));
        sw.Stop();

        if (completed != task)
        {
            steps.Add(new { step = stepName, ok = false, elapsedMs = sw.ElapsedMilliseconds, detail = "timeout" });
            return (false, default);
        }

        var value = await task;
        steps.Add(new { step = stepName, ok = true, elapsedMs = sw.ElapsedMilliseconds, detail = "ok" });
        return (true, value);
    }
    catch (Exception ex)
    {
        sw.Stop();
        steps.Add(new { step = stepName, ok = false, elapsedMs = sw.ElapsedMilliseconds, detail = ex.GetType().Name + ": " + ex.Message });
        return (false, default);
    }
}

public sealed record RegisterRequest(string Email, string Password);
