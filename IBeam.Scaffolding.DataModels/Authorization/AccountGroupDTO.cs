using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;
using System;

namespace IBeam.Scaffolding.DataModels
{

    [Serializable]
	[Alias("AccountGroup")]
	public class AccountGroupDTO : IEntity
	{

		public Guid Id { get; set; }
		public Guid ApplicationId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
    }
}
