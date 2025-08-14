using System;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Models
{	public class AccountDevice : IAccountDevice
	{

		public Guid Id { get; set; }
		public Guid AccountId { get; set; }
		public string PinToken { get; set; }
		public string DeviceToken { get; set; }
		public DateTime DateCreated { get; set; }

	}
}
