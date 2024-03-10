using System;
namespace IBeam.Models
{	public class AccountRole : IAccountRole
	{

		public Guid Id { get; set; }
		public Guid AccountId { get; set; }
		public string AccountName { get; set; }
		public string DisplayName { get; set; }
		public Guid ApplicationRoleId { get; set; }
		public string ApplicationRoleName { get; set; }

	}
}
