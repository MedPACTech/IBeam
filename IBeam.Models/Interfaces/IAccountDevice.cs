using System;

namespace IBeam.Models.Interfaces
{
	public interface IAccountDevice
	{

		 Guid Id { get; set; }
		 Guid AccountId { get; set; }
		 string PinToken { get; set; }
		 string DeviceToken { get; set; }
		 DateTime DateCreated { get; set; }

	}
}
