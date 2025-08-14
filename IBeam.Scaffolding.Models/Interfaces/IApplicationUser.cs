using System;

namespace IBeam.Scaffolding.Models.Interfaces
{
	public interface IApplicationAccount
	{

		 Guid Id { get; set; }
		 Guid ApplicationId { get; set; }
		 Guid AccountId { get; set; }
		 Guid LicenseId { get; set; }
		 DateTime DateLicensed { get; set; }

	}
}
