using IBeam.DataModels;
using IBeam.Utilities;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Data;
using IBeam.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;
using Microsoft.Extensions.Options;

namespace IBeam.Repositories
{
    public class NotificationRepository : BaseRepository<NotificationDTO>, INotificationRepository
	{
        public NotificationRepository(IOptions<AppSettings> appSettings, IMemoryCache memorycache) : base(appSettings, memorycache){

        }

        public IEnumerable<NotificationDTO> GetByAccount(Guid AccountId, bool isRead = false)
        {
            try
            {
                using var db = _dataFactory.OpenDbConnection();
                return db.Select<NotificationDTO>(x => x.AccountId == AccountId && x.IsRead == isRead);
            }
            catch(Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByAccount", AccountId, isRead);
            }
        }

        public IEnumerable<NotificationDTO> GetByAccounts(IEnumerable<Guid> AccountIds, bool isRead = false)
        {
            try
            {
                using var db = _dataFactory.OpenDbConnection();
                return db.Select<NotificationDTO>(x => Sql.In(x.AccountId, AccountIds) && x.IsRead == isRead);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByAccounts", AccountIds, isRead);
            }
        }

        public bool CheckDuplicate(NotificationDTO notification)
        {
            try
            {
                using var db = _dataFactory.OpenDbConnection();
                if(db.Select<NotificationDTO>(x => x.AccountId == notification.AccountId && !x.IsRead && x.Message == notification.Message).Any())
                {
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "CheckDuplicate", null);
            }
        }

        public void SaveAsRead(Guid id)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                db.UpdateOnly(() => new NotificationDTO { IsRead = true }, where: q => q.Id == id);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "Archive", id);
            }
        }
    }
}
