using IBeam.Scaffolding.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Repositories.Interfaces;
namespace IBeam.Scaffolding.Repositories.Interfaces
{
    public interface INotificationRepository : IBaseRepository<NotificationDTO>
    {
        IEnumerable<NotificationDTO> GetByAccount(Guid AccountId, bool isRead = false);
        IEnumerable<NotificationDTO> GetByAccounts(IEnumerable<Guid> AccountIds, bool isRead = false);
        void SaveAsRead(Guid id);
 
    }
}