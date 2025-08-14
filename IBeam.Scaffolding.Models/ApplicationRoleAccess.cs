using System;
namespace IBeam.Scaffolding.Models
{	public class ApplicationRoleAccess : IApplicationRoleAccess
	{

		public Guid Id { get; set; }
		public Guid ApplicationRoleId { get; set; }
		public string RoleName { get; set; }
		public string RoleDescripition { get; set; }
		public string ServiceName { get; set; }
		public string ActionName { get; set; }

	}
}
