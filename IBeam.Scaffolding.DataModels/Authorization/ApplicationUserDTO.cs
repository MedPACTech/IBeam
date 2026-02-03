using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;
using System;

namespace IBeam.Scaffolding.DataModels
{

    [Serializable]
	[Alias("ApplicationAccounts")]
	public class ApplicationAccountDTO : IEntity
	{

		public Guid Id { get; set; }
		public Guid ApplicationId { get; set; }
		public Guid AccountId { get; set; }
		public Guid LicenseId { get; set; }
		public DateTime DateLicensed { get; set; }
        public bool IsDeleted { get; set; }
    }
}
