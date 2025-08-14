using System;
using System.Collections.Generic;
using IBeam.Scaffolding.Models;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Services.Interfaces
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
