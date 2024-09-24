using System;
using AutoMapper;
using IBeam.DataModels;
using IBeam.Models;
using IBeam.Models.Interfaces;
using IBeam.Repositories.Interfaces;
using IBeam.Services.Interfaces;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace IBeam.Services.System
{

    public class SystemAuditService : ISystemAuditService
    {
        private readonly ISystemAuditRepository _systemAuditRepository;
        private IMapper _mapper;

        public SystemAuditService(IMapper mapper, ISystemAuditRepository systemAuditRepository)
        {
            _mapper = mapper;
            _systemAuditRepository = systemAuditRepository;
        }

        //TODO: we should add paging but add this as metadata to all objects : int pageSize, int pageNumber, pageCount
        public IEnumerable<ISystemAudit> Fetch()
        {
            var systemAuditDTOs = _systemAuditRepository.GetAll().OrderBy(x => x.DateChanged);

            return _mapper.Map<IEnumerable<SystemAudit>>(systemAuditDTOs);
        }

        public void LogCreate(Guid entityId, string entityName, object dataObject)
        {
            LogAudit(entityId, entityName, "Create", dataObject);
        }

        public void LogUpdate(Guid entityId, string entityName, object dataObject)
        {
            LogAudit(entityId, entityName, "Update", dataObject);
        }

        public void LogArchive(Guid entityId, string entityName, object dataObject)
        {
            LogAudit(entityId, entityName, "Archive", dataObject);
        }

        public void LogDelete(Guid entityId, string entityName, object dataObject)
        {
            LogAudit(entityId, entityName, "Delete", dataObject);
        }

        public void LogAudit(Guid entityId, string entityName, string changeType, object dataObject)
        {
            var systemAuditDTO = GenerateAuditLog(entityId, entityName, changeType, dataObject);
            systemAuditDTO.Id = Guid.NewGuid();

            _systemAuditRepository.Save(systemAuditDTO);
        }

        //TODO: should add the who is doing the modification
        private SystemAuditDTO GenerateAuditLog(Guid entityId, string entityName, string changeType, object dataObject)
        {
            return new SystemAuditDTO()
            {
                EntityID = entityId,
                DateChanged = DateTime.Now,
                ChangeType = changeType,
                EntityName = entityName,
                Data = JsonSerializer.Serialize(dataObject)
            };
        }
    }
}
