using IBeam.Api.Models;
using IBeam.Management.API.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Management.API;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var setup = args.Contains("--setup", StringComparer.Ordinal);
        var builder = WebApplication.CreateBuilder(args.Where(x => x != "--setup" && x != "--configure-customer-demo").ToArray());
        if (builder.Environment.IsDevelopment())
        {
            var path = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_LOCAL_CONFIG")
                ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../.local/management/settings.json"));
            builder.Configuration.AddJsonFile(path, optional: true, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();
        }
        if (args.Contains("--customer-target") || args.Contains("--setup-customer-target")) {
            var flag = args.Contains("--setup-customer-target") ? "--setup-customer-target" : "--customer-target";
            var index = Array.IndexOf(args, flag);
            if (index + 1 >= args.Length) throw new InvalidOperationException("Synthetic target key required.");
            await Demo.DemoCustomerTarget.RunAsync(builder.Configuration, builder.Environment, args[index + 1], flag == "--setup-customer-target"); return;
        }
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Limits.MaxRequestLineSize = 32768;
            o.Limits.MaxRequestHeadersTotalSize = 32768;
            o.Limits.MaxRequestBodySize = 16384;
        });

        if (setup)
        {
            await TrustedSetup.RunAsync(builder.Configuration, builder.Environment);
            return;
        }

        builder.Services.AddManagementHost(builder.Configuration, builder.Environment);
        var app = builder.Build();
        if (args.Contains("--configure-customer-demo")) { await Demo.CustomerDemoInstallation.ConfigureAsync(app.Services, builder.Configuration, builder.Environment); await app.DisposeAsync(); return; }
        // No request/response body, Authorization header, query-string, OTP, or provider-exception logging.
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch
            {
                if (context.Response.HasStarted) { context.Abort(); return; }
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsJsonAsync(new ApiResponse<object>
                {
                    Success = false, TraceId = context.TraceIdentifier,
                    Errors = [new ApiError { Code = "Management.Unavailable", Message = "The management service is temporarily unavailable." }]
                });
            }
        });
        app.UseRouting();
        app.UseCors(HostRegistration.CorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "IBeam.Management.API", iBeamVersion = "2.16.0-dev.tcm.117.1", dataMode = builder.Configuration.GetValue<bool>("ManagementHost:Demo:Enabled") ? "synthetic-demo" : "connected-targets" })).AllowAnonymous();
        if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
        app.MapControllers();
        await app.RunAsync();
    }
}
