using ServiceStack.OrmLite;
using System;
using IBeam.DataModels;
using IBeam.Utilities;
using System.Linq;
using IBeam.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using IBeam.DataModels.System;

namespace IBeam.Repositories
{
    public class SystemAuditRepository : BaseRepository<SystemAuditDTO>, ISystemAuditRepository
    {
        public SystemAuditRepository(TenantContext tenantContext, IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(tenantContext, appSettings, memoryCache)
        {
        }
    }
}