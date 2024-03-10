using AutoMapper;
using IBeam.DataModels;
using System;
using IBeam.Services.Interfaces;
using IBeam.Repositories.Interfaces;
using IBeam.Models;
using IBeam.Services.Authorization;
using System.Collections.Generic;
using System.Linq;

namespace IBeam.Services
{

    public class NotificationService : INotificationService
    {
        private readonly IMapper _mapper;
        private readonly INotificationRepository _notificationRepository;
        private readonly IAccountService _AccountService;
        private readonly int _notificationThresholdCalendarMinutes;

        public NotificationService(IMapper mapper, INotificationRepository notificationRepository, IAccountService AccountService)
        {
            _mapper = mapper;
            _notificationRepository = notificationRepository;
            _AccountService = AccountService;
            _notificationThresholdCalendarMinutes = 1440;
        }

        public INotification Fetch(Guid id)
        {

            if (id == Guid.Empty)
                return new Notification();
            else
            {
                var notificationDTO = _notificationRepository.GetById(id);
                return _mapper.Map<Notification>(notificationDTO);
            }
        }
        public List<Notification> FetchByAccount(Guid AccountId)
        {
            if (AccountId == Guid.Empty)
            {
                return new List<Notification>();
            }
            else
            {
                var notificationDTOs = _notificationRepository.GetByAccount(AccountId);
                return _mapper.Map<List<Notification>>(notificationDTOs);
            }
        }

        public void Save(INotification notification)
        {

            var notifications = new List<NotificationDTO>();
            foreach (var id in notification.AccountIds)
            {
                var reviewNotification = new NotificationDTO
                {
                    Id = Guid.NewGuid(),
                    AccountId = id,
                    NotificationTypeId = notification.NotificationTypeId,
                    NotificationType = notification.NotificationType,
                    Message = notification.Message,
                    NotificationDate = notification.NotificationDate,
                    IsRead = false
                };
                notifications.Add(reviewNotification);
            }
            _notificationRepository.SaveAll(notifications);
        }

        public Guid Delete(Guid id)
        {
           _notificationRepository.DeleteById(id);
            return id;
        }

        public Guid SaveAsRead(Guid id)
        {
            _notificationRepository.SaveAsRead(id);
            return id;
        }

        public void SaveNotification(INotification notification, bool allowDuplicate = false)
        {
            var notificationDTOs = new List<NotificationDTO>();
            var existingNotificationDTOs = FetchByAccounts(notification.AccountIds);


            foreach (var AccountId in notification.AccountIds)
            {
                var notificationDTO = _mapper.Map<NotificationDTO>(notification);
                notificationDTO.AccountId = AccountId;
                var duplicateFound = allowDuplicate == false && HasDuplicateNotification(notificationDTO, existingNotificationDTOs);

                if (duplicateFound == false)
                        notificationDTOs.Add(notificationDTO);

            }
            _notificationRepository.SaveAll(notificationDTOs);
        }

        private IEnumerable<NotificationDTO> FetchByAccounts(IEnumerable<Guid> AccountIds)
        {
            var notificationDTOs = _notificationRepository.GetByAccounts(AccountIds);
            return notificationDTOs;
        }

        private bool HasDuplicateNotification(NotificationDTO notificationDTO, IEnumerable<NotificationDTO> existingNotificationDTOs)
        {
            var AccountNotifications = existingNotificationDTOs.Where(x => x.AccountId == notificationDTO.AccountId);

            var duplicateNotifications = AccountNotifications.Where(x => x.NotificationTypeId == notificationDTO.NotificationTypeId
            && x.Message == notificationDTO.Message
            && (x.NotificationDate - notificationDTO.NotificationDate).TotalMinutes <= _notificationThresholdCalendarMinutes).ToList();

            var any = duplicateNotifications.Any();
            return any;
        }

    }
}
