using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;
using System;

namespace IBeam.Scaffolding.DataModels
{

    [Serializable]
	[Alias("Licenses")]
	public class LicenseDTO : IEntity
	{

		public Guid Id { get; set; }
		public Guid ApplicationId { get; set; }
		public string LicenseData { get; set; }
		public DateTime DateActive { get; set; }
        public bool IsDeleted { get; set; }
    }
}
