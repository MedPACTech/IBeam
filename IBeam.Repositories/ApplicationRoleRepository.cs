using Microsoft.Extensions.Caching.Memory;
using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.Utilities;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Options;
using IBeam.DataModels.System;

namespace IBeam.Repositories
{
	public class ApplicationRoleRepository : BaseRepository<ApplicationRoleDTO>, IApplicationRoleRepository
	{
        public ApplicationRoleRepository(TenantContext tenantContext, IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(tenantContext, appSettings, memoryCache)
        {
        }

        public IEnumerable<ApplicationRoleDTO> GetByApplicationId(Guid applicationId)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                return db.Select<ApplicationRoleDTO>(x => x.ApplicationId == applicationId);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByApplicationId");
            }
        }
    }
}
