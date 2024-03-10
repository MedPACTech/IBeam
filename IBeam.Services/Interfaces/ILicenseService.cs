using System;
using IBeam.Models.Interfaces;

namespace IBeam.Services.Interfaces
{
	public interface ILicenseService
	{
		ILicense GetLatest();
		ILicense Fetch(Guid id);
        void Save(ILicense license);

	}
}
