using ServiceStack.DataAnnotations;
using System;

namespace IBeam.DataModels
{

	[Serializable]
	[Alias("ApplicationRoleAccess")]
	public class ApplicationRoleAccessDTO : IDTO
	{

		public Guid Id { get; set; }
		public Guid ApplicationRoleId { get; set; }
		public string RoleName { get; set; }
		public string RoleDescripition { get; set; }
		public string ServiceName { get; set; }
		public string ActionName { get; set; }

	}
}
