using IBeam.DataModels;
using System;
using System.Collections.Generic;

namespace IBeam.Repositories.Interfaces
{
    public interface INotificationRepository : IRepository<NotificationDTO>
    {
        IEnumerable<NotificationDTO> GetByAccount(Guid AccountId, bool isRead = false);
        IEnumerable<NotificationDTO> GetByAccounts(IEnumerable<Guid> AccountIds, bool isRead = false);
        void SaveAsRead(Guid id);
 
    }
}