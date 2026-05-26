namespace IBeam.Identity.Interfaces;

using IBeam.Identity.Models;

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