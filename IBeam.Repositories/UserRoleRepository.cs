using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.Utilities;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IBeam.Repositories
{
	public class AccountRoleRepository : BaseRepository<AccountRoleDTO>, IAccountRoleRepository
	{
        public AccountRoleRepository(IOptions<AppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache)
        {

        }
        public IEnumerable<AccountRoleDTO> GetByAccountId(Guid AccountId)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                return db.Select<AccountRoleDTO>(x => x.AccountId == AccountId);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByAccountId", null, AccountId);
            }
        }
    }
}
