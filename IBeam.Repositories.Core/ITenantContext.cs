namespace IBeam.Repositories.Abstractions;

public interface ITenantContext
{
    Guid? TenantId { get; }
    bool IsTenantIdSet();
}
