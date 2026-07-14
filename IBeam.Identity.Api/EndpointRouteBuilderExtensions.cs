using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace IBeam.Identity.Api;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapIBeamAccessControlApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllers();
        return endpoints;
    }
}

