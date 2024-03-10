using ServiceStack.DataAnnotations;
using System;

namespace IBeam.DataModels
{

	[Serializable]
	[Alias("AccountGroupMember")]
	public class AccountGroupMemberDTO : IDTO
	{

		public Guid Id { get; set; }
		public Guid AccountGroupId { get; set; }
		public Guid AccountId { get; set; }
		public string AccountName { get; set; }
		public string DisplayName { get; set; }

	}
}
