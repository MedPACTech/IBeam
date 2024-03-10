using System;
using System.Collections.Generic;
using IBeam.Models;

namespace IBeam.Services.Interfaces
{
	public interface INotificationService
	{
        INotification Fetch(Guid id);
        void Save(INotification notification);
		List<Notification> FetchByAccount(Guid AccountId);
		Guid Delete(Guid id);
		Guid SaveAsRead(Guid id);
		void SaveNotification(INotification notification, bool allowDuplicate = false);
	}
}
