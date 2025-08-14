// IBeam.Services/IAuditService.cs
using IBeam.DataModels.System;

namespace IBeam.Services.Abstractions
{
    public interface IEntityAuditService<TDTO> : IAuditService where TDTO : IDTO
    {
        void LogCreate(TDTO dto);
        void LogUpdate(TDTO dto);
        void LogDelete(TDTO dto);
    }
}
