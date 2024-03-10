using System;

namespace IBeam.Models
{
	public interface IApplicationRoleAccess
	{

		 Guid Id { get; set; }
		 Guid ApplicationRoleId { get; set; }
		 string RoleName { get; set; }
		 string RoleDescripition { get; set; }
		 string ServiceName { get; set; }
		 string ActionName { get; set; }

	}
}
