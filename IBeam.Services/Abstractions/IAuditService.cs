// IBeam.Services/IAuditService.cs
using System.Threading;
using System.Threading.Tasks;
using IBeam.Utilities.Auditing;

namespace IBeam.Services.Abstractions
{
    public interface IAuditService
    {
        void LogAudit(AuditEvent ev);
    }
}
