using ServiceStack.DataAnnotations;
using System;

namespace IBeam.DataModels
{

	[Serializable]
	[Alias("AccountGroup")]
	public class AccountGroupDTO : IDTO
	{

		public Guid Id { get; set; }
		public Guid ApplicationId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public bool IsActive { get; set; }

	}
}
