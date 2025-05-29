using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.Utilities;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Options;

using Microsoft.Extensions.Caching.Memory;
using IBeam.DataModels.System;

namespace IBeam.Repositories
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
