using Microsoft.Extensions.Caching.Memory;
using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using IBeam.Utilities;
using Microsoft.Extensions.Options;
using IBeam.DataModels.System;

namespace IBeam.Repositories
{
	public class ApplicationRepository : BaseRepository<ApplicationDTO>, IApplicationRepository
	{
		public ApplicationRepository(TenantContext tenantContext, IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(tenantContext, appSettings, memoryCache)
		{
		}
	}
}
