using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Diagnostics.Api;

internal sealed class ManagementAuthenticationSchemeValidator(
    IAuthenticationSchemeProvider schemes,
    IOptions<ManagementDiagnosticsApiOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (await schemes.GetSchemeAsync(options.Value.AuthenticationScheme).ConfigureAwait(false) is null)
            throw new InvalidOperationException("The configured diagnostics management authentication scheme is not registered.");
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
