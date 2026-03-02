namespace IBeam.Api.Middleware;

public static class ApiExceptionApplicationBuilderExtensions
{
    public static IApplicationBuilder UseApiExceptionMiddleware(this IApplicationBuilder app)
        => app.UseMiddleware<ApiExceptionMiddleware>();
}
