using IBeam.Models;
using System;
using System.Collections.Generic;

namespace IBeam.Services.Interfaces
{
    public interface ISystemAuditService
    {
        IEnumerable<ISystemAudit> Fetch();
        void LogAudit(Guid entityId, string entityName, string changeType, object dataObject);
        void LogArchive(Guid entityId, string entityName, object dataObject);
        void LogCreate(Guid entityId, string entityName, object dataObject);
        void LogDelete(Guid entityId, string entityName, object dataObject);
        void LogUpdate(Guid entityId, string entityName, object dataObject);

    }
}