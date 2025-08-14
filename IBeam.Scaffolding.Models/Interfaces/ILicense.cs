using System;

namespace IBeam.Scaffolding.Models.Interfaces
{
	public interface ILicense
	{

		 Guid Id { get; set; }
		 Guid ApplicationId { get; set; }
		 string LicenseData { get; set; }
		 DateTime DateActive { get; set; }

	}
}
