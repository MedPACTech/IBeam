using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.Utilities;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IBeam.Repositories
{
	public class AccountGroupMemberRepository : BaseRepository<AccountGroupMemberDTO>, IAccountGroupMemberRepository
	{
        public AccountGroupMemberRepository(IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache)
        {
        }

        public IEnumerable<AccountGroupMemberDTO> GetByAccountGroupId(Guid AccountGroupId)
        {
            try
            {
                using var db = _dataFactory.OpenDbConnection();
                return db.Select<AccountGroupMemberDTO>(x => x.AccountGroupId == AccountGroupId);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByAccountGroupId");
            }
        }

        public IEnumerable<AccountGroupMemberDTO> GetByAccountId(Guid AccountId)
        {
            try
            {
                using var db = _dataFactory.OpenDbConnection();
                return db.Select<AccountGroupMemberDTO>(x => x.AccountId == AccountId);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByAccountId");
            }
        }

        public void RemoveAccountFromGroup(Guid AccountId, Guid AccountGroupId)
        {
            try
            {
                using var db = _dataFactory.OpenDbConnection();
                db.Delete<AccountGroupMemberDTO>(x => x.AccountId == AccountId && x.AccountGroupId == AccountGroupId);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "RemoveAccountFromGroup", null, AccountId, AccountGroupId);
            }
        }

    }
}
