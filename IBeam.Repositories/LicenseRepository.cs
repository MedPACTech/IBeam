using System;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.Utilities;
using ServiceStack.OrmLite;
using Microsoft.Extensions.Options;

namespace IBeam.Repositories
{
	public class LicenseRepository : BaseRepository<LicenseDTO>, ILicenseRepository
	{
		public LicenseRepository(IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(appSettings, memoryCache)
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
