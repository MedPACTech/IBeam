using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;
using System;

namespace IBeam.Scaffolding.DataModels
{

    [Serializable]
	[Alias("ApplicationRole")]
	public class ApplicationRoleDTO : IDTO
	{

		public Guid Id { get; set; }
		public Guid ApplicationId { get; set; }
		public string RoleName { get; set; }
		public string Descripition { get; set; }
		public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
    }
}
