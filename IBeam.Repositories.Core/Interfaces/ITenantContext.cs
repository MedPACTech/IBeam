namespace IBeam.Repositories.Core;

public interface ITenantContext
{
    Guid? TenantId { get; }
    bool IsTenantIdSet();
}
