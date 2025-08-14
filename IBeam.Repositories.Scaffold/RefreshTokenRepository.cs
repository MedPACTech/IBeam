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
using IBeam.Utilities.Exceptions;

namespace IBeam.Scaffolding.Repositories
{
    public class RefreshTokenRepository : BaseRepository<RefreshTokenDTO>, IRefreshTokenRepository
    {
        public RefreshTokenRepository(TenantContext tenantContext, IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(tenantContext, appSettings, memoryCache)
        {
        }

        public RefreshTokenDTO GetByToken(string token)
        {
            try
            {
                using var db = _dataFactory.OpenDbConnection();
                return db.Select<RefreshTokenDTO>(x => x.Token == token).FirstOrDefault();
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByToken", null, token);
            }
        }
    }
}