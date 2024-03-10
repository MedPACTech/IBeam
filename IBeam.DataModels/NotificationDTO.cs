using ServiceStack.DataAnnotations;
using System;

namespace IBeam.DataModels
{
	[Serializable]
	[Alias("Notification")]
	public class NotificationDTO : IDTO
	{
		public Guid Id { get; set; }
		public Guid AccountId { get; set; }
		public Guid NotificationTypeId { get; set; }
		public string NotificationType { get; set; }
		public string Message { get; set; }
		public DateTime NotificationDate { get; set; }
		public bool IsRead { get; set; }
	}
}
