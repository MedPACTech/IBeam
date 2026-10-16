using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Invites;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Management.Identity.Services;

/// <summary>Reuse Identity invite delivery, sanitizing sender failures before its nested operation/audit pipeline sees them.</summary>
public sealed class ManagementSupportInvitationDelivery
{
    private readonly TenantInviteService _inner;
    public ManagementSupportInvitationDelivery(IServiceProvider provider)
        => _inner = ActivatorUtilities.CreateInstance<TenantInviteService>(provider,
            new SafeSender(provider.GetRequiredService<IIdentityCommunicationSender>()));
    public Task<TenantInviteCreatedResult> CreateAsync(Guid org, TenantInviteCreateRequest request, Guid actor, CancellationToken ct)
        => _inner.CreateInviteAsync(org, request, actor, ct);
    private sealed class SafeSender(IIdentityCommunicationSender sender) : IIdentityCommunicationSender
    {
        public async Task SendAsync(IdentitySenderMessage message, CancellationToken ct = default)
        {
            try { await sender.SendAsync(message, ct); }
            catch (OperationCanceledException) { throw new OperationCanceledException("Support invitation delivery was cancelled.", ct); }
            catch { throw new ManagementSupportUnavailableException(); }
        }
    }
}
