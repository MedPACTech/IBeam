using IBeam.Utilities.Auditing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IBeam.Services.Abstractions
{
    public interface IAuditServiceAsync 
    {
        Task LogAsync(AuditEvent ev, CancellationToken ct = default);
    }
}
