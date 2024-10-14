using Microsoft.Extensions.Caching.Memory;
using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.Utilities;
using ServiceStack.OrmLite;
using System;
using System.Data;
using System.Linq;
using Microsoft.Extensions.Options;

namespace IBeam.Repositories
{
    public class AccountContextRepository : BaseRepository<AccountContextDTO>, IAccountContextRepository
	{
        public AccountContextRepository(IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache)
        {
        }

        public AccountContextDTO GetByAccountId(Guid AccountId)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                return db.Select<AccountContextDTO>(x => x.AccountId == AccountId).FirstOrDefault(); 
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByAccountId");
            }
        }
    }
}
