using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;
using System;

namespace IBeam.Scaffolding.DataModels
{

    [Serializable]
	[Alias("AccountGroupRole")]
	public class AccountGroupRoleDTO : IDTO
	{

		public Guid Id { get; set; }
		public Guid AccountGroupId { get; set; }
		public string AccountGroupName { get; set; }
		public Guid ApplicationRoleId { get; set; }
		public string ApplicationRoleName { get; set; }
        public bool IsDeleted { get; set; }
    }
}
