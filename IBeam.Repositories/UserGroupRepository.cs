using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.API.Utilities;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Options;

using Microsoft.Extensions.Caching.Memory;

namespace IBeam.Repositories
{
    public class AccountGroupRepository : BaseRepository<AccountGroupDTO>, IAccountGroupRepository
    {
        public AccountGroupRepository(IOptions<AppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache)
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
