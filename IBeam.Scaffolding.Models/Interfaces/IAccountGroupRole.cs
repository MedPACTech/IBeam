using System;

namespace IBeam.Scaffolding.Models
{
	public interface IAccountGroupRole
	{

		 Guid Id { get; set; }
		 Guid AccountGroupId { get; set; }
		 string AccountGroupName { get; set; }
		 Guid ApplicationRoleId { get; set; }
		 string ApplicationRoleName { get; set; }

	}
}
