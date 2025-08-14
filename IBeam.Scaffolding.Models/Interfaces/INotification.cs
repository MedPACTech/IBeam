using System;
using System.Collections.Generic;

namespace IBeam.Scaffolding.Models
{
	public interface INotification
	{
		 Guid Id { get; set; }
		 IEnumerable<Guid> AccountIds { get; set; }
		 Guid NotificationTypeId { get; set; }
		 string NotificationType { get; set; }
		 string Message { get; set; }
		 DateTime NotificationDate { get; set; }
		 bool IsRead { get; set; }
	}
}
