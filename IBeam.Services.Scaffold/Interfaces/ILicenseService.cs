using System;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Services.Interfaces
{
	public interface ILicenseService
	{
		ILicense GetLatest();
		ILicense Fetch(Guid id);
        void Save(ILicense license);

	}
}
