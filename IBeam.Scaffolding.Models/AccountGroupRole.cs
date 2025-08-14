using System;
namespace IBeam.Scaffolding.Models
{	public class AccountGroupRole : IAccountGroupRole
	{

		public Guid Id { get; set; }
		public Guid AccountGroupId { get; set; }
		public string AccountGroupName { get; set; }
		public Guid ApplicationRoleId { get; set; }
		public string ApplicationRoleName { get; set; }

	}
}
