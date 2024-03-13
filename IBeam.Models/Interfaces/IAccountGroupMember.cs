using System;

namespace IBeam.Models
{
	public interface IAccountGroupMember
	{

		 Guid Id { get; set; }
		 Guid AccountGroupId { get; set; }
		 Guid AccountId { get; set; }
		 string AccountName { get; set; }
		 string DisplayName { get; set; }

	}
}
