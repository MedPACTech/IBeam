using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Repositories.Interfaces;
using IBeam.Utilities;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Options;

using Microsoft.Extensions.Caching.Memory;
using IBeam.DataModels.System;
using IBeam.Repositories;

namespace IBeam.Scaffolding.Repositories
{
    public class AccountGroupRepository : BaseRepository<AccountGroupDTO>, IAccountGroupRepository
    {
        public AccountGroupRepository(TenantContext tenantContext, IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(tenantContext, appSettings, memoryCache)
        {
        }

        //public IEnumerable<AccountGroupDTO> GetByAccountId(Guid AccountId)
        //{
        //    try
        //    {
        //        using var db = _dataFactory.OpenDbConnection();
        //        return db.Select<AccountGroupDTO>(x => x. == AccountId);
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new RepositoryException(ex, RepositoryName, "GetByAccountId");
        //    }
        //}   
    }
}
