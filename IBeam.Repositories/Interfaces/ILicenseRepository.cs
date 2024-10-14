using IBeam.DataModels;

namespace IBeam.Repositories.Interfaces
{
	public interface ILicenseRepository : IBaseRepository<LicenseDTO>
	{
		LicenseDTO GetLatest();
	}
}
