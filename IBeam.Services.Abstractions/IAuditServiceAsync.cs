using System.Threading;
using System.Threading.Tasks;

namespace IBeam.Services.Abstractions
{
    public interface IAuditServiceAsync
    {
        Task LogAuditAsync(object auditEvent, CancellationToken ct = default);
    }
}
