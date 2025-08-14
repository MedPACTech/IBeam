using Microsoft.Extensions.Caching.Memory;
using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Repositories.Interfaces;
using IBeam.Utilities;
using Microsoft.Extensions.Options;
using IBeam.DataModels.System;
using IBeam.Repositories;

namespace IBeam.Scaffolding.Repositories
{
	public class ApplicationAccountRepository : BaseRepository<ApplicationAccountDTO>, IApplicationAccountRepository
	{
		public ApplicationAccountRepository(TenantContext tenantContext, IOptions<BaseAppSettings> appSettings, IMemoryCache memoryCache) : base(tenantContext, appSettings, memoryCache)
		{

        }
	}
}
