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
    public class RefreshTokenRepository : BaseRepository<RefreshTokenDTO>, IRefreshTokenRepository
    {
        public RefreshTokenRepository(IOptions<AppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache)
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