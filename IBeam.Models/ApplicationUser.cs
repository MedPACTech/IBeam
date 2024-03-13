using System;
using IBeam.Models.Interfaces;

namespace IBeam.Models
{	public class ApplicationAccount : IApplicationAccount
	{

		public Guid Id { get; set; }
		public Guid ApplicationId { get; set; }
		public Guid AccountId { get; set; }
		public Guid LicenseId { get; set; }
		public DateTime DateLicensed { get; set; }

	}
}
