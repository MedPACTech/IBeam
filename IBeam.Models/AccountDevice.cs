using System;
using IBeam.Models.Interfaces;

namespace IBeam.Models
{	public class AccountDevice : IAccountDevice
	{

		public Guid Id { get; set; }
		public Guid AccountId { get; set; }
		public string PinToken { get; set; }
		public string DeviceToken { get; set; }
		public DateTime DateCreated { get; set; }

	}
}
