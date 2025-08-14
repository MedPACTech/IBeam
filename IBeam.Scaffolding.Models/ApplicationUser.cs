using System;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Models
{	public class ApplicationAccount : IApplicationAccount
	{

		public Guid Id { get; set; }
		public Guid ApplicationId { get; set; }
		public Guid AccountId { get; set; }
		public Guid LicenseId { get; set; }
		public DateTime DateLicensed { get; set; }

	}
}
