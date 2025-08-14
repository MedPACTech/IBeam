// IBeam.Services/IAuditService.cs
using System.Threading;
using System.Threading.Tasks;
using IBeam.Utilities.Auditing;

namespace IBeam.Services.Abstractions
{
    public interface IAuditService
    {
        void LogAudit(Guid entityId, string entityName, string changeType, object payload);
        void LogAudit(AuditEvent ev);
    }
}
