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
using IBeam.Services.Authorization;

namespace IBeam.Services.System
{

    public class ServiceAuthorizationService : IServiceAuthorizationService
    // ISystemAuthorizationService
    {
        private readonly IApplicationRoleAccessRepository _applicationRoleAccessRepository;
        private readonly IApplicationRoleService _applicationRoleService;
        private readonly IMapper _mapper;

        /// <summary>
        /// System level service designed to read-only fetch roles allowed for any application
        /// </summary>
        /// <param name="applicationRoleAccessRepository">applicationRoleAccess repository to avoid circular refrences</param>
        /// <param name="applicationRoleService">access all available roles for an application</param>
        public ServiceAuthorizationService(IMapper mapper, IApplicationRoleAccessRepository applicationRoleAccessRepository,
            IApplicationRoleService applicationRoleService)
        {
            _mapper = mapper;
            _applicationRoleAccessRepository = applicationRoleAccessRepository;
            _applicationRoleAccessRepository.EnableCache = true;
            _applicationRoleService = applicationRoleService;
        }

        public IEnumerable<IServiceAuthorization> Fetch()
        {
            var applicationRoleAccessDTOs = _applicationRoleAccessRepository.GetAll();
            return _mapper.Map<IEnumerable<ServiceAuthorization>>(applicationRoleAccessDTOs);
        }

        //TODO: perhaps add ApplicationId on the RoleAccess DTO, however this should be cached data
        public IEnumerable<IServiceAuthorization> FetchByApplication(Guid applicationId)
        {
            var applicationRoleIds = _applicationRoleService.FetchByApplicationId(applicationId).Select(x => x.Id);
            var applicationRoleAccessDTOs = _applicationRoleAccessRepository.GetByApplicationRoleIds(applicationRoleIds);
            return _mapper.Map<IEnumerable<ServiceAuthorization>>(applicationRoleAccessDTOs);
        }
    }
}
