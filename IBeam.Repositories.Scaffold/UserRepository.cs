using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Repositories.Interfaces;
using IBeam.Utilities;
using ServiceStack.OrmLite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using IBeam.DataModels.System;
using IBeam.Repositories;

namespace IBeam.Scaffolding.Repositories
{
	public class AccountRepository : BaseRepository<AccountDTO>, IAccountRepository
	{
		public AccountRepository(TenantContext tenantContext, IOptions<BaseAppSettings> appSettings, IMemoryCache memorycache) : base(tenantContext, appSettings, memorycache)
        {

        }

        public override List<AccountDTO> GetAll(bool withArchived = false)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                return db.Select<AccountDTO>(x => !x.IsArchived);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetAll");
            }
        }

        public IEnumerable<AccountDTO> GetArchived()
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                return db.Select<AccountDTO>(x => x.IsArchived);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetAll");
            }
        }

        public AccountDTO GetByEmail(string email)
        {
            try
            {
                using var db = _dataFactory.OpenDbConnection();
                return db.Select<AccountDTO>(x => x.Email == email).FirstOrDefault();
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByEmail", null, email);
            }
        }
    }
}
