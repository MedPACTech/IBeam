using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.Extensions.Logging;

namespace IBeam.Identity.Seeder;

internal sealed class LocalSeedIdentityCommunicationSender : IIdentityCommunicationSender
{
    private readonly ILogger<LocalSeedIdentityCommunicationSender> _logger;

    public LocalSeedIdentityCommunicationSender(ILogger<LocalSeedIdentityCommunicationSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(IdentitySenderMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Local seed communication suppressed. Channel={Channel} Destination={Destination} Purpose={Purpose}",
            message.Channel,
            message.Destination,
            message.Purpose);

        return Task.CompletedTask;
    }
}
