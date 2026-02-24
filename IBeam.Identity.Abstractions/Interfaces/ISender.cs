namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface ISender
{
    Task SendAsync(
        SenderChannel channel,
        string destination,
        string code,
        SenderPurpose purpose,
        Guid? tenantId,
        CancellationToken ct = default);
}