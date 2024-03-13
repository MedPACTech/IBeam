using AutoMapper;
using IBeam.DataModels;
using IBeam.Models;
using IBeam.Repositories.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IBeam.Services.Authorization
{

    public class ApplicationRoleAccessService : IApplicationRoleAccessService
    {
        private readonly IMapper _mapper;
        private readonly IApplicationRoleAccessRepository _applicationRoleAccessRepository;
        private readonly IApplicationRoleService _applicationRoleService;

        public ApplicationRoleAccessService(IMapper mapper, IApplicationRoleAccessRepository applicationRoleAccessRepository, 
            IApplicationRoleService applicationRoleService)
        {
            _mapper = mapper;
            _applicationRoleAccessRepository = applicationRoleAccessRepository;
            _applicationRoleAccessRepository.EnableCache = true;
            _applicationRoleService = applicationRoleService;
        }

        public IApplicationRoleAccess Fetch(Guid id)
        {
            if (id == Guid.Empty)
                return new ApplicationRoleAccess();
            else
            {
                var applicationRoleAccessDTO = _applicationRoleAccessRepository.GetById(id);
                return _mapper.Map<ApplicationRoleAccess>(applicationRoleAccessDTO);
            }
        }

        public IEnumerable<IApplicationRoleAccess> Fetch()
        {
            var applicationRoleAccessDTOs = _applicationRoleAccessRepository.GetAll();
            return _mapper.Map<IEnumerable<ApplicationRoleAccess>>(applicationRoleAccessDTOs);
        }

        //TODO: perhaps add ApplicationId on the RoleAccess DTO, however this should be cached data
        public IEnumerable<IApplicationRoleAccess> FetchByApplication(Guid applicationId)
        {
            var applicationRoleIds = _applicationRoleService.FetchByApplicationId(applicationId).Select(x=>x.Id);
            var applicationRoleAccessDTOs = _applicationRoleAccessRepository.GetByApplicationRoleIds(applicationRoleIds);
            return _mapper.Map<IEnumerable<ApplicationRoleAccess>>(applicationRoleAccessDTOs);
        }

        public void Save(IApplicationRoleAccess applicationRoleAccess)
        {
            if (applicationRoleAccess.Id == Guid.Empty)
                applicationRoleAccess.Id = Guid.NewGuid();

            var applicationRoleAccessDTO = _mapper.Map<ApplicationRoleAccessDTO>(applicationRoleAccess);
            _applicationRoleAccessRepository.Save(applicationRoleAccessDTO);
        }

    }
}
