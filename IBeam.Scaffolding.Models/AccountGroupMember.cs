using System;
namespace IBeam.Scaffolding.Models
{	public class AccountGroupMember : IAccountGroupMember
	{

		public Guid Id { get; set; }
		public Guid AccountGroupId { get; set; }
		public Guid AccountId { get; set; }
		public string AccountName { get; set; }
		public string DisplayName { get; set; }

	}
}
