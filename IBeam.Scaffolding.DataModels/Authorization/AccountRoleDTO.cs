using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;
using System;

namespace IBeam.Scaffolding.DataModels
{

    [Serializable]
	[Alias("AccountRole")]
	public class AccountRoleDTO : IEntity
	{

		public Guid Id { get; set; }
		public Guid AccountId { get; set; }
		public string AccountName { get; set; }
		public string DisplayName { get; set; }
		public Guid ApplicationRoleId { get; set; }
		public string ApplicationRoleName { get; set; }
        public bool IsDeleted { get; set; }
    }
}
