using ServiceStack.OrmLite;
using System;
using IBeam.Scaffolding.DataModels;
using IBeam.Utilities;
using System.Linq;
using IBeam.Scaffolding.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using IBeam.DataModels.System;
using IBeam.Repositories;
using IBeam.Scaffolding.DataModels;

namespace IBeam.Scaffolding.Repositories
{
    public class SystemAuditRepository : BaseRepository<SystemAuditDTO>, ISystemAuditRepository
    {
        public SystemAuditRepository(TenantContext tenantContext, IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(tenantContext, appSettings, memoryCache)
        {
        }
    }
}