using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;
using System;

namespace IBeam.Scaffolding.DataModels
{

    [Serializable]
	[Alias("ApplicationRoleAccess")]
	public class ApplicationRoleAccessDTO : IEntity
	{

		public Guid Id { get; set; }
		public Guid ApplicationRoleId { get; set; }
		public string RoleName { get; set; }
		public string RoleDescripition { get; set; }
		public string ServiceName { get; set; }
		public string ActionName { get; set; }
        public bool IsDeleted { get; set; }
    }
}
