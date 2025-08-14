using System;

namespace IBeam.Scaffolding.Models
{
	public interface IAccountRole
	{

		 Guid Id { get; set; }
		 Guid AccountId { get; set; }
		 string AccountName { get; set; }
		 string DisplayName { get; set; }
		 Guid ApplicationRoleId { get; set; }
		 string ApplicationRoleName { get; set; }

	}
}
