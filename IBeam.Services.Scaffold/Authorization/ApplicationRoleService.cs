using AutoMapper;
using IBeam.Repositories;
using IBeam.Scaffolding.DataModels;
using System;
using System.Collections.Generic;
using IBeam.Scaffolding.Services.Interfaces;
using IBeam.Repositories.Interfaces;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Repositories.Interfaces;
using IBeam.Scaffolding.Models;

namespace IBeam.Scaffolding.Services.Authorization
{

	public class ApplicationRoleService : IApplicationRoleService
	{
        private readonly IMapper _mapper;
        private readonly IApplicationRoleRepository _applicationRoleRepository;

        public ApplicationRoleService(IMapper mapper, IApplicationRoleRepository applicationRoleRepository)
        {
            _mapper = mapper;
            _applicationRoleRepository = applicationRoleRepository;
        }

        public IApplicationRole Fetch(Guid id)
        {

            if (id == Guid.Empty)
                return new ApplicationRole();
            else
            {
                var applicationRoleDTO = _applicationRoleRepository.GetById(id);
                return _mapper.Map<ApplicationRole>(applicationRoleDTO);
            }
        }

        public IEnumerable<IApplicationRole> FetchByApplicationId(Guid applicationId)
        {
           var applicationRoleDTO = _applicationRoleRepository.GetByApplicationId(applicationId);
           return _mapper.Map<IEnumerable<ApplicationRole>>(applicationRoleDTO);
        }

        public void Save(IApplicationRole applicationRole)
        {
            if (applicationRole.Id == Guid.Empty)
                applicationRole.Id = Guid.NewGuid();

            var applicationRoleDTO = _mapper.Map<ApplicationRoleDTO>(applicationRole);
            _applicationRoleRepository.Save(applicationRoleDTO);
        }

    }
}
