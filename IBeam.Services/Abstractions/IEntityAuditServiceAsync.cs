// IBeam.Services/IAuditService.cs
using IBeam.DataModels.System;

namespace IBeam.Services.Abstractions
{
    public interface IEntityAuditServiceAsync<TDTO> : IAuditServiceAsync where TDTO : IEntity
    {
        Task LogCreateAsync(TDTO dto, CancellationToken ct = default);
        Task LogUpdateAsync(TDTO dto, CancellationToken ct = default);
        Task LogDeleteAsync(TDTO dto, CancellationToken ct = default);
    }
}
