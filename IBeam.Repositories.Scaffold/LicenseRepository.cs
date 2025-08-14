using System;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Repositories.Interfaces;
using IBeam.Utilities;
using ServiceStack.OrmLite;
using Microsoft.Extensions.Options;
using IBeam.DataModels.System;
using IBeam.Repositories;
using IBeam.Utilities.Exceptions;

namespace IBeam.Scaffolding.Repositories
{
	public class LicenseRepository : BaseRepository<LicenseDTO>, ILicenseRepository
	{
		public LicenseRepository(TenantContext tenantContext, IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(tenantContext, appSettings, memoryCache)
        {

        }

        public LicenseDTO GetLatest()
        {
            try
            {
                using var db = _dataFactory.OpenDbConnection();
                return db.Select(db.From<LicenseDTO>().OrderByDescending(x => x.DateActive )).FirstOrDefault();
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetLatest", null);
            }
        }
    }
}
