using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.API.Utilities;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IBeam.Repositories
{
    public class ApplicationRoleAccessRepository : BaseRepository<ApplicationRoleAccessDTO>, IApplicationRoleAccessRepository
	{
        public ApplicationRoleAccessRepository(IOptions<AppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache)
        {

        }

        public IEnumerable<ApplicationRoleAccessDTO> GetByApplicationRoleIds(IEnumerable<Guid> applicationRoleIds)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                return db.Select<ApplicationRoleAccessDTO>(x=> Sql.In(x.ApplicationRoleId, applicationRoleIds));
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByApplicationRoleIds");
            }
        }
    }
}
