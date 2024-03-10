using System;
namespace IBeam.Models
{	public class ApplicationRole : IApplicationRole
	{

		public Guid Id { get; set; }
		public Guid ApplicationId { get; set; }
		public string RoleName { get; set; }
		public string Descripition { get; set; }
		public bool IsActive { get; set; }

	}
}
