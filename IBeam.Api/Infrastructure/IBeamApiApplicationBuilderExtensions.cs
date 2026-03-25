using IBeam.Api.Middleware;

namespace IBeam.Api.Infrastructure;

public static class IBeamApiApplicationBuilderExtensions
{
    public static IApplicationBuilder UseIBeamApi(this IApplicationBuilder app)
    {
        app.UseApiExceptionMiddleware();
        return app;
    }
}
