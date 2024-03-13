using ServiceStack.DataAnnotations;
using System;

namespace IBeam.DataModels
{

	[Serializable]
	[Alias("AccountDevices")]
	public class AccountDeviceDTO : IDTO
	{

		public Guid Id { get; set; }
		public Guid AccountId { get; set; }
		public string PinToken { get; set; }
		public string DeviceToken { get; set; }
		public DateTime DateCreated { get; set; }

	}
}
