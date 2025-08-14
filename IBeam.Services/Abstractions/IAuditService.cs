// IBeam.Services/IAuditService.cs
using System.Threading;
using System.Threading.Tasks;
using IBeam.Utilities;

namespace IBeam.Services.Abstractions
{
    public interface IAuditService
    {
        Task LogAsync(AuditEvent evt, CancellationToken ct = default);
    }
}
