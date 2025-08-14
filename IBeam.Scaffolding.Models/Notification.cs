using System;
using System.Collections.Generic;

namespace IBeam.Scaffolding.Models
{	public class Notification : INotification
	{
		public Guid Id { get; set; }
		public IEnumerable<Guid> AccountIds { get; set; }
		public Guid NotificationTypeId { get; set; }
		public string NotificationType { get; set; }
		public string Message { get; set; }
		public DateTime NotificationDate { get; set; }
		public bool IsRead { get; set; }
	}

}
