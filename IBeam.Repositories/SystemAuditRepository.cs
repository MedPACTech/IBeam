using ServiceStack.OrmLite;
using System;
using IBeam.DataModels;
using IBeam.Utilities;
using System.Linq;
using IBeam.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IBeam.Repositories
{
    public class SystemAuditRepository : BaseRepository<SystemAuditDTO>, ISystemAuditRepository
    {
        public SystemAuditRepository(IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache)
        {
        }
    }
}