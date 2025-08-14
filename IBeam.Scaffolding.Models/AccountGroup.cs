using System;
namespace IBeam.Scaffolding.Models
{	public class AccountGroup : IAccountGroup
	{

		public Guid Id { get; set; }
		public Guid ApplicationId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public bool IsActive { get; set; }

	}
}
