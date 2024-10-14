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
	public class AccountGroupRoleRepository : BaseRepository<AccountGroupRoleDTO>, IAccountGroupRoleRepository
	{
    public AccountGroupRoleRepository(IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache)
    {
    }

        public IEnumerable<AccountGroupRoleDTO> GetByAccountGroupId(Guid AccountGroupId)
        {
            try
            {
                using var db = _dataFactory.OpenDbConnection();
                return db.Select<AccountGroupRoleDTO>(x => x.AccountGroupId == AccountGroupId);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByAccountGroupId");
            }
        }

        public IEnumerable<AccountGroupRoleDTO> GetByAccountGroupIds(IEnumerable<Guid> AccountGroupIds)
        {
            try
            {
                using var db = _dataFactory.OpenDbConnection();
                return db.Select<AccountGroupRoleDTO>(x=> Sql.In(x.AccountGroupId, AccountGroupIds));
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByAccountGroupIds");
            }
        }
    }
}
