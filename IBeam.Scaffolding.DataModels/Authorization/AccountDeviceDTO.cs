using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;
using System;

namespace IBeam.Scaffolding.DataModels
{

    [Serializable]
	[Alias("AccountDevices")]
	public class AccountDeviceDTO : IEntity
	{

		public Guid Id { get; set; }
		public Guid AccountId { get; set; }
		public string PinToken { get; set; }
		public string DeviceToken { get; set; }
		public DateTime DateCreated { get; set; }
        public bool IsDeleted { get; set; }
    }
}
