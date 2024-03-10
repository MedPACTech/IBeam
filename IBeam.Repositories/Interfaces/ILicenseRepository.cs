using IBeam.DataModels;

namespace IBeam.Repositories.Interfaces
{
	public interface ILicenseRepository : IRepository<LicenseDTO>
	{
		LicenseDTO GetLatest();
	}
}
